using Microsoft.Extensions.Hosting;

namespace AgenticPitstop.Infrastructure;

/// <summary>
/// Self-wiring runtime for the API Configurator.
///
/// Everything it needs is built here from services ASP.NET Core always
/// registers, so Program.cs needs no edit. The outbox drain also starts itself
/// on first use rather than as a hosted service.
/// </summary>
public static class ApiConfigRuntime
{
    private static readonly object Gate = new();

    private static ApiConfigDataAccess _db;
    private static ApiConfigSecretResolver _secrets;
    private static ApiConfigQueuePublisher _queue;
    private static ApiConfigExecutionEngine _engine;
    private static Task _drainLoop;

    public static ApiConfigDataAccess Db(IConfiguration config)
    {
        if (_db != null) return _db;
        lock (Gate) { _db ??= new ApiConfigDataAccess(config); }
        return _db;
    }

    public static ApiConfigSecretResolver Secrets(IConfiguration config, ILoggerFactory loggers)
    {
        if (_secrets != null) return _secrets;
        lock (Gate)
        {
            _secrets ??= new ApiConfigSecretResolver(
                config, loggers.CreateLogger<ApiConfigSecretResolver>());
        }
        return _secrets;
    }

    public static ApiConfigQueuePublisher Queue(IConfiguration config, ILoggerFactory loggers)
    {
        if (_queue != null) return _queue;
        lock (Gate)
        {
            _queue ??= new ApiConfigQueuePublisher(
                config, loggers.CreateLogger<ApiConfigQueuePublisher>());
        }
        return _queue;
    }

    public static ApiConfigExecutionEngine Engine(IConfiguration config, ILoggerFactory loggers,
                                                  IHttpClientFactory httpClientFactory)
    {
        if (_engine != null) return _engine;
        lock (Gate)
        {
            _engine ??= new ApiConfigExecutionEngine(
                Db(config), httpClientFactory, Secrets(config, loggers), config,
                loggers.CreateLogger<ApiConfigExecutionEngine>());
        }
        return _engine;
    }

    /// <summary>
    /// Starts the outbox drain once, on the first request that touches the
    /// screen. Cancelled by the host's ApplicationStopping token.
    /// </summary>
    public static void EnsureDrainStarted(IConfiguration config, ILoggerFactory loggers,
                                          IHttpClientFactory httpClientFactory,
                                          IHostApplicationLifetime lifetime)
    {
        if (_drainLoop != null) return;

        lock (Gate)
        {
            if (_drainLoop != null) return;

            var logger = loggers.CreateLogger("ApiConfigRuntime");
            var token = lifetime.ApplicationStopping;
            var seconds = Math.Max(1, config.GetValue("ApiConfigurator:OutboxPollSeconds", 5));

            _drainLoop = Task.Run(async () =>
            {
                while (!token.IsCancellationRequested)
                {
                    try
                    {
                        await DrainAsync(config, loggers, httpClientFactory, token);
                    }
                    catch (OperationCanceledException) when (token.IsCancellationRequested)
                    {
                        break;
                    }
                    catch (Exception ex)
                    {
                        // The loop must never die on a bad row.
                        logger.LogError(ex, "API Configurator outbox poll failed.");
                    }

                    try { await Task.Delay(TimeSpan.FromSeconds(seconds), token); }
                    catch (OperationCanceledException) { break; }
                }
            }, token);
        }
    }

    private static async Task DrainAsync(IConfiguration config, ILoggerFactory loggers,
                                         IHttpClientFactory httpClientFactory, CancellationToken token)
    {
        var db = Db(config);
        var publisher = Queue(config, loggers);
        var engine = Engine(config, loggers, httpClientFactory);

        var pending = db.Table("dbo.API_Config_Outbox_GetPending", ApiConfigDataAccess.P("@Top", 10));

        foreach (System.Data.DataRow row in pending.Rows)
        {
            token.ThrowIfCancellationRequested();

            var outboxId = ApiConfigDataAccess.Int(row, "OutboxId");
            var executionId = ApiConfigDataAccess.Int(row, "ExecutionId");
            var payload = ApiConfigDataAccess.Str(row, "PayloadJson");

            try
            {
                if (!await publisher.PublishAsync(executionId, payload, token))
                    await engine.ProcessAsync(executionId, token);

                db.Execute("dbo.API_Config_Outbox_MarkPublished",
                    ApiConfigDataAccess.P("@OutboxId", outboxId),
                    ApiConfigDataAccess.P("@Succeeded", true),
                    ApiConfigDataAccess.P("@Error", DBNull.Value));
            }
            catch (Exception ex)
            {
                db.Execute("dbo.API_Config_Outbox_MarkPublished",
                    ApiConfigDataAccess.P("@OutboxId", outboxId),
                    ApiConfigDataAccess.P("@Succeeded", false),
                    ApiConfigDataAccess.P("@Error", ex.Message.Length > 1900 ? ex.Message[..1900] : ex.Message));
            }
        }
    }
}