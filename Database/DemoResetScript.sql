/* ===========================================================================
   APS demo reset. Master data is never deleted.
   Run Section 1 alone to clear runtime data, or 1+2+3 for a clean start.
   =========================================================================== */
SET XACT_ABORT ON;
SET NOCOUNT ON;
BEGIN TRANSACTION;

-- ===========================================================================
-- SECTION 1: runtime data (uploads, records, reviews, runs, logs)
-- ===========================================================================

-- 1a. Rows the pipelines wrote into generated collection tables.
DECLARE @sql nvarchar(max) = N'';
SELECT @sql += N'DELETE FROM dbo.' + QUOTENAME(m.TableName) + N';'
FROM   dbo.APS_CollectionMaster m
WHERE  OBJECT_ID(N'dbo.' + QUOTENAME(m.TableName), N'U') IS NOT NULL;
IF @sql <> N'' EXEC sys.sp_executesql @sql;

-- Legacy collection tables, if they still hold demo rows.
IF OBJECT_ID(N'dbo.APS_Collection_Inflow', N'U') IS NOT NULL      DELETE FROM dbo.APS_Collection_Inflow;
IF OBJECT_ID(N'dbo.APS_Collection_ImagePortal', N'U') IS NOT NULL DELETE FROM dbo.APS_Collection_ImagePortal;

-- 1b. Pipeline runs and per-record state.
DELETE FROM dbo.API_Config_PipelineRunItem;
DELETE FROM dbo.API_Config_PipelineRecordState;
DELETE FROM dbo.API_Config_PipelineRun;
UPDATE dbo.API_Config_Pipeline SET LastRunOn = NULL;

-- 1c. Inflow records and their reviews.
DELETE FROM dbo.APS_Review;
DELETE FROM dbo.APS_InflowDataArchive;
DELETE FROM dbo.APS_InflowData;
DELETE FROM dbo.APS_Inflow;

-- 1d. Agent run history.
DELETE FROM dbo.APS_AgentExecution;

-- 1e. User audit trail (the users themselves are kept).
DELETE FROM dbo.APS_UserAudit;

-- Identity counters start from 1 again, so demo IDs stay tidy.
DBCC CHECKIDENT ('dbo.APS_Inflow',                     RESEED, 0) WITH NO_INFOMSGS;
DBCC CHECKIDENT ('dbo.APS_InflowData',                 RESEED, 0) WITH NO_INFOMSGS;
DBCC CHECKIDENT ('dbo.API_Config_PipelineRun',         RESEED, 0) WITH NO_INFOMSGS;
DBCC CHECKIDENT ('dbo.API_Config_PipelineRunItem',     RESEED, 0) WITH NO_INFOMSGS;

-- ===========================================================================
-- SECTION 2: configuration (templates, screens, collections, pipelines)
-- Comment this section out to keep your configuration.
-- ===========================================================================

-- 2a. Pipelines.
DELETE FROM dbo.API_Config_PipelineStep;
DELETE FROM dbo.API_Config_Pipeline;

-- 2b. Generated collection tables are dropped, then their definitions.
DECLARE @drop nvarchar(max) = N'';
SELECT @drop += N'DROP TABLE dbo.' + QUOTENAME(m.TableName) + N';'
FROM   dbo.APS_CollectionMaster m
WHERE  OBJECT_ID(N'dbo.' + QUOTENAME(m.TableName), N'U') IS NOT NULL
  AND  m.TableName LIKE 'APS[_]Collection[_]%';
IF @drop <> N'' EXEC sys.sp_executesql @drop;

DELETE FROM dbo.APS_CollectionColumn;
DELETE FROM dbo.APS_CollectionMaster;

-- 2c. Detail layout (new tables, then the legacy ones).
DELETE FROM dbo.APS_DetailLayoutTile;
DELETE FROM dbo.APS_DetailLayoutTab;
DELETE FROM dbo.APS_DetailLayout;
DELETE FROM dbo.APS_DetailField;
DELETE FROM dbo.APS_DetailTab;
DELETE FROM dbo.APS_DetailConfig;

-- 2d. Inbox layout.
DELETE FROM dbo.APS_InboxColumn;
DELETE FROM dbo.APS_InboxConfig;

-- 2e. Inflow templates.
DELETE FROM dbo.APS_ConfigHeader;
DELETE FROM dbo.APS_InflowColumnConfig;
DELETE FROM dbo.APS_InflowConfig;

-- 2f. Configuration history.
DELETE FROM dbo.APS_ConfigHistory;

-- ===========================================================================
-- SECTION 3: API Configurator (connectors and executions)
-- Comment this section out to keep your connectors.
-- ===========================================================================

DELETE FROM dbo.API_Config_ExecutionException;
DELETE FROM dbo.API_Config_ExecutionAttempt;
DELETE FROM dbo.API_Config_ExecutionApproval;
DELETE FROM dbo.API_Config_OutboxMessage;
DELETE FROM dbo.API_Config_ExecutionRequest;
DELETE FROM dbo.API_Config_CheckRun;
DELETE FROM dbo.API_Config_AuditEvent;

UPDATE dbo.API_Config_Connector SET CurrentPublishedVersionId = NULL;

DELETE FROM dbo.API_Config_AllowedHost;
DELETE FROM dbo.API_Config_SuccessCode;
DELETE FROM dbo.API_Config_AuthConfig;
DELETE FROM dbo.API_Config_QueryParameter;
DELETE FROM dbo.API_Config_Header;
DELETE FROM dbo.API_Config_ConnectorVersion;
DELETE FROM dbo.API_Config_Connector;
DELETE FROM dbo.API_Config_Application;

COMMIT TRANSACTION;

-- What is left
SELECT 'Clients' AS MasterTable, COUNT(*) AS Rows FROM dbo.APS_ClientMaster
UNION ALL SELECT 'Processes',            COUNT(*) FROM dbo.APS_ProcessMaster
UNION ALL SELECT 'Use cases',            COUNT(*) FROM dbo.APS_UseCaseMaster
UNION ALL SELECT 'Users',                COUNT(*) FROM dbo.APS_UserMaster
UNION ALL SELECT 'Roles',                COUNT(*) FROM dbo.APS_RoleMaster
UNION ALL SELECT 'User use-case map',    COUNT(*) FROM dbo.APS_UserUseCaseMapping
UNION ALL SELECT 'Checklist questions',  COUNT(*) FROM dbo.APS_ChecklistQuestionMaster
UNION ALL SELECT 'Source registry',      COUNT(*) FROM dbo.APS_SourceRegistry
UNION ALL SELECT 'Categories',           COUNT(*) FROM dbo.APS_ManageCategory
UNION ALL SELECT 'Reports',              COUNT(*) FROM dbo.APS_ReportMaster
UNION ALL SELECT 'API providers',        COUNT(*) FROM dbo.API_Config_Provider;