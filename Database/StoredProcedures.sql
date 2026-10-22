Text
---------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------

CREATE   PROCEDURE dbo.APS_Agent_Delete
    @agid     smallint,
    @actionby varchar(20)
AS
BEGIN
    SET NOCOUNT ON;
    UPDATE dbo.APS_Agent
    SET    activestatus = 0, modifiedby = @actionby, modifiedon = GETDATE()
    WHERE  agid = @agid;
END


Text
---------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------

/* =============================================================================
   8. AGENTS
   ============================================================================= */
CREATE   PROCEDURE dbo.APS_Agent_Get
AS
BEGIN
    SET NOCOUNT ON;
    SELECT
 a.agid, a.agentname, a.purpose, a.apiendpoint, a.apiparams,
           a.scheduletype, a.scheduletime, a.everyminutes, a.activestatus,
           a.modifiedby, a.modifiedon,
           CASE WHEN a.scheduletype = 2
                THEN 'Every ' + CAST(a.e
veryminutes AS varchar(3)) + ' minutes'
                ELSE 'Daily at ' + ISNULL(a.scheduletime, '--:--') END AS scheduletext,
           e.startedon       AS lastexecution,
           e.status          AS laststatus,
           e.durationseconds AS last
duration,
           e.recordcount     AS lastrecordcount
    FROM   dbo.APS_Agent a
    OUTER APPLY (SELECT TOP 1 x.startedon, x.status, x.durationseconds, x.recordcount
                 FROM   dbo.APS_AgentExecution x
                 WHERE  x.agid = a.
agid
                 ORDER BY x.aeid DESC) e
    ORDER BY a.agentname;
END


Text
---------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------

CREATE   PROCEDURE dbo.APS_Agent_GetById
    @agid smallint
AS
BEGIN
    SET NOCOUNT ON;
    SELECT agid, agentname, purpose, apiendpoint, apiparams, scheduletype,
           scheduletime, everyminutes, activestatus, modifiedby, modifiedon
    FROM   dbo
.APS_Agent WHERE agid = @agid;
END


Text
---------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------

CREATE   PROCEDURE dbo.APS_Agent_Save
    @agid         smallint,          -- 0 to insert
    @agentname    nvarchar(200),
    @purpose      nvarchar(1000),
    @apiendpoint  nvarchar(1000),
    @apiparams    nvarchar(MAX) = NULL,
    @scheduletype tinyi
nt,
    @scheduletime varchar(5) = NULL,
    @everyminutes tinyint = NULL,
    @activestatus tinyint = 1,
    @actionby     varchar(20),
    @newagid      smallint OUTPUT,
    @message      nvarchar(200) OUTPUT
AS
BEGIN
    SET NOCOUNT ON;
    SET @newagi
d = 0;

    IF @agentname IS NULL OR LTRIM(RTRIM(@agentname)) = ''
    BEGIN
        SET @message = 'Agent name is required.';
        RETURN;
    END
    IF @scheduletype = 2 AND (@everyminutes IS NULL OR @everyminutes <= 0)
    BEGIN
        SET @messag
e = 'Frequency in minutes must be a positive number.';
        RETURN;
    END
    IF EXISTS (SELECT 1 FROM dbo.APS_Agent WHERE agentname = @agentname AND agid <> @agid)
    BEGIN
        SET @message = 'An agent with this name already exists.';
        R
ETURN;
    END

    IF @agid = 0
    BEGIN
        INSERT INTO dbo.APS_Agent (agentname, purpose, apiendpoint, apiparams, scheduletype,
                                   scheduletime, everyminutes, activestatus, modifiedby, modifiedon)
        VALUES (@a
gentname, @purpose, @apiendpoint, @apiparams, @scheduletype,
                @scheduletime, @everyminutes, @activestatus, @actionby, GETDATE());
        SET @newagid = CAST(SCOPE_IDENTITY() AS smallint);
        SET @message = 'Agent scheduled.';
    END

    ELSE
    BEGIN
        UPDATE dbo.APS_Agent
        SET    agentname = @agentname, purpose = @purpose, apiendpoint = @apiendpoint,
               apiparams = @apiparams, scheduletype = @scheduletype, scheduletime = @scheduletime,
               everym
inutes = @everyminutes, activestatus = @activestatus,
               modifiedby = @actionby, modifiedon = GETDATE()
        WHERE  agid = @agid;
        SET @newagid = @agid;
        SET @message = 'Agent updated.';
    END
END


Text
---------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------

CREATE   PROCEDURE dbo.APS_AgentExecution_Get
    @agid smallint = NULL,
    @top  int = 50
AS
BEGIN
    SET NOCOUNT ON;
    SELECT TOP (@top) e.aeid, e.agid, a.agentname, e.startedon, e.durationseconds,
           e.status, e.recordcount, e.message
    
FROM   dbo.APS_AgentExecution e
    JOIN   dbo.APS_Agent a ON a.agid = e.agid
    WHERE (@agid IS NULL OR e.agid = @agid)
    ORDER BY e.aeid DESC;
END


Text
---------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------

CREATE   PROCEDURE dbo.APS_AgentExecution_Log
    @agid            smallint,
    @startedon       datetime,
    @durationseconds int,
    @status          varchar(30),
    @recordcount     int = NULL,
    @message         nvarchar(2000) = NULL
AS
BEGIN
 
   SET NOCOUNT ON;
    INSERT INTO dbo.APS_AgentExecution (agid, startedon, durationseconds, status, recordcount, message)
    VALUES (@agid, @startedon, @durationseconds, @status, @recordcount, @message);
END


Text
---------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------
CREATE PROCEDURE dbo.APS_AgentRunningStatus
    @Status VARCHAR(10) = NULL
AS
BEGIN
    SET NOCOUNT ON;

    SELECT *
    FROM APS_AgentExecution
    WHERE @Status IS NULL
       OR Status = @Status;
END;

Text
---------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------
CREATE PROCEDURE dbo.APS_ChecklistQuestionMaster_Delete
(
    @QuestionID INT
)
AS
BEGIN
    SET NOCOUNT ON;

    BEGIN TRY

        -- Check if record exists
        IF NOT EXISTS
        (
            SELECT 1
            FROM dbo.APS_ChecklistQuestionMaster
            WHERE QuestionID = @QuestionID
        )
        BEGIN
            RAISERROR ('Checklist Question record not found.', 16, 1);
            RETURN;
        END

        -- Delete the record
        UPDATE dbo.APS_ChecklistQuestionMaster  SET Status = 0
        WHERE QuestionID = @QuestionID

        SELECT
            1 AS Success,
            'Checklist Question deleted successfully.' AS Message;

    END TRY
    BEGIN CATCH

        SELECT
            0 AS Success,
            ERROR_MESSAGE() AS Message;

    END CATCH
END;

Text
---------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------
CREATE PROCEDURE APS_ChecklistQuestionMaster_Get  
AS    
BEGIN    
    SET NOCOUNT ON;    
    
    SELECT *    
    FROM dbo.APS_ChecklistQuestionMaster WHERE Status = 1    
END; 

Text
---------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------
CREATE PROCEDURE APS_ChecklistQuestionMaster_GetById
    @QuestionId INT = NULL  
AS  
BEGIN  
    SET NOCOUNT ON;  
  
    SELECT *  
    FROM dbo.APS_ChecklistQuestionMaster  
    WHERE (QuestionId IS NULL OR QuestionId = @QuestionId);  
END; 

Text
---------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------
CREATE PROCEDURE APS_ChecklistQuestionMaster_Save  
(  
    @QuestionId            INT = NULL,  
    @Question              VARCHAR(1000),  
    @Category              VARCHAR(100) = NULL,  
    @Status                VARCHAR(50) = NULL,  
    @SourceApplicability   VARCHAR(255) = NULL,  
    @InstructionPrompt     VARCHAR(4000) = NULL,  
    @BusinessRulesPrompt   VARCHAR(4000) = NULL,  
    @OutputType            VARCHAR(100) = NULL,  
    @DisplayOrder          INT = NULL,  
    @DeveloperNotes        VARCHAR(2000) = NULL  
)  
AS  
BEGIN  
    SET NOCOUNT ON;  
  
    -- INSERT  
    IF ISNULL(@QuestionId, 0) = 0  
    BEGIN  
        INSERT INTO APS_ChecklistQuestionMaster  
        (  
            Question,  
            Category,  
            Status,  
            SourceApplicability,  
            InstructionPrompt,  
            BusinessRulesPrompt,  
            OutputType,  
            DisplayOrder,  
            DeveloperNotes  
        )  
        VALUES  
        (  
            @Question,  
            @Category,  
            @Status,  
            @SourceApplicability,  
            @InstructionPrompt,  
            @BusinessRulesPrompt,  
            @OutputType,  
            @DisplayOrder,  
            @DeveloperNotes  
        );  
  
        SELECT SCOPE_IDENTITY() AS QuestionId,  
               'Inserted Successfully' AS Message;  
    END  
    ELSE  
    BEGIN  
        -- Validate QuestionId exists  
        IF NOT EXISTS  
        (  
            SELECT 1  
            FROM APS_ChecklistQuestionMaster  
            WHERE QuestionId = @QuestionId  
        )  
        BEGIN  
            RAISERROR('QuestionId does not exist.', 16, 1);  
            RETURN;  
        END;  
  
        -- UPDATE  
        UPDATE APS_ChecklistQuestionMaster  
        SET  
            Question = @Question,  
            Category = @Category,  
            Status = @Status,  
            SourceApplicability = @SourceApplicability,  
            InstructionPrompt = @InstructionPrompt,  
            BusinessRulesPrompt = @BusinessRulesPrompt,  
            OutputType = @OutputType,  
            DisplayOrder = @DisplayOrder,  
            DeveloperNotes = @DeveloperNotes  
        WHERE QuestionId = @QuestionId;  
  
        SELECT @QuestionId AS QuestionId,  
               'Updated Successfully' AS Message;  
    END  
END; 

Text
---------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------

/* =============================================================================
   MASTERS AND LOOKUPS
   ============================================================================= */

CREATE   PROCEDURE dbo.APS_ClientMaster_Get
    @ActiveOnly tinyi
nt = 1
AS
BEGIN
    SET NOCOUNT ON;
    SELECT ClientID, ClientCode, ClientName, IsActive
    FROM   dbo.APS_ClientMaster
    WHERE  (@ActiveOnly = 0 OR IsActive = 1)
    ORDER BY ClientName;
END


Text
---------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------

/* =============================================================================
   COLLECTIONS
   ============================================================================= */

CREATE   PROCEDURE dbo.APS_Collection_Get
    @CollectionID int = NULL
AS

BEGIN
    SET NOCOUNT ON;
    SELECT c.CollectionID, c.CollectionName, c.TableName, c.Description,
           c.UseCaseID, u.UseCaseCode, c.IsGenerated, c.GeneratedOn,
           c.CreatedBy, c.CreatedDate,
           ColumnCount = (SELECT COUNT(*) FROM 
dbo.APS_CollectionColumn cc
                          WHERE cc.CollectionID = c.CollectionID AND cc.IsActive = 1)
    FROM   dbo.APS_CollectionMaster c
    LEFT   JOIN dbo.APS_UseCaseMaster u ON u.UseCaseID = c.UseCaseID
    WHERE  c.IsActive = 1
      AN
D (@CollectionID IS NULL OR c.CollectionID = @CollectionID)
    ORDER BY c.CollectionName;
END


Text
---------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------

CREATE   PROCEDURE dbo.APS_Collection_MarkGenerated
    @CollectionID int,
    @ActionBy     varchar(30)
AS
BEGIN
    SET NOCOUNT ON;
    UPDATE dbo.APS_CollectionMaster
       SET IsGenerated = 1, GeneratedOn = GETDATE(),
           ModifiedBy = @Action
By, ModifiedDate = GETDATE()
     WHERE CollectionID = @CollectionID;
END


Text
---------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------

/* --- Save a collection definition -----------------------------------------
   Definition only. No DDL runs here: generating the table is a separate,
   explicit step, so a mistyped column can be corrected before it becomes a
   real object. */
CREATE 
  PROCEDURE dbo.APS_Collection_Save
    @CollectionID   int,
    @CollectionName varchar(128),
    @TableName      varchar(128),
    @Description    varchar(1000) = NULL,
    @UseCaseID      int = NULL,
    @Columns        dbo.APS_CollectionColumnType REA
DONLY,
    @ActionBy       varchar(30),
    @NewCollectionID int OUTPUT,
    @Message        varchar(200) OUTPUT
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;
    SET @NewCollectionID = 0;

    IF LEN(LTRIM(RTRIM(ISNULL(@CollectionName, '')))) = 0
 
   BEGIN
        SET @Message = 'Enter a collection name.';
        RETURN;
    END

    -- Every generated object carries the prefix, so a collection can never
    -- create a table that looks like it belongs to another system.
    IF LEFT(@TableName, 6)
 <> 'APS_'
        SET @TableName = 'APS_' + @TableName;

    IF EXISTS (SELECT 1 FROM dbo.APS_CollectionMaster
               WHERE TableName = @TableName AND CollectionID <> @CollectionID)
    BEGIN
        SET @Message = 'A collection already uses that
 table name.';
        RETURN;
    END

    IF NOT EXISTS (SELECT 1 FROM @Columns WHERE IsIdentityColumn = 1)
    BEGIN
        SET @Message = 'The first column must be an identity primary key.';
        RETURN;
    END

    IF EXISTS (SELECT 1 FROM @Colu
mns
               WHERE ConstraintType = 'FK'
                 AND (RefTableName IS NULL OR RefColumnName IS NULL
                      OR LEN(LTRIM(RTRIM(RefTableName))) = 0
                      OR LEN(LTRIM(RTRIM(RefColumnName))) = 0))
    BEGIN
     
   SET @Message = 'A foreign key column needs both a reference table and a reference column.';
        RETURN;
    END

    BEGIN TRY
        BEGIN TRANSACTION;

        IF @CollectionID = 0
        BEGIN
            INSERT INTO dbo.APS_CollectionMaster
 
               (CollectionName, TableName, Description, UseCaseID, CreatedBy)
            VALUES (@CollectionName, @TableName, @Description, @UseCaseID, @ActionBy);

            SET @NewCollectionID = CAST(SCOPE_IDENTITY() AS int);
        END
        ELS
E
        BEGIN
            UPDATE dbo.APS_CollectionMaster
               SET CollectionName = @CollectionName, TableName = @TableName,
                   Description = @Description, UseCaseID = @UseCaseID,
                   ModifiedBy = @ActionBy, Modi
fiedDate = GETDATE()
             WHERE CollectionID = @CollectionID;

            SET @NewCollectionID = @CollectionID;
        END

        -- Low-volume definition rows: delete and re-insert is simpler and safer
        -- than diff logic, and it runs 
inside the same transaction.
        DELETE FROM dbo.APS_CollectionColumn WHERE CollectionID = @NewCollectionID;

        INSERT INTO dbo.APS_CollectionColumn
            (CollectionID, ColumnName, DataTypeName, ColumnLength, NumericPrecision, NumericScal
e,
             IsNullable, ConstraintType, DefaultValue, CheckExpression, RefTableName, RefColumnName,
             IsIdentityColumn, IsSystemColumn, SortOrder, CreatedBy)
        SELECT @NewCollectionID, ColumnName, DataTypeName, ColumnLength, NumericPr
ecision, NumericScale,
               IsNullable, ConstraintType, DefaultValue, CheckExpression, RefTableName, RefColumnName,
               IsIdentityColumn, IsSystemColumn, SortOrder, @ActionBy
        FROM   @Columns;

        EXEC dbo.APS_ConfigHistor
y_Insert
             @UseCaseID = @UseCaseID,
             @ConfigItem = 'Collection',
             @Detail = @TableName,
             @ActionBy = @ActionBy;

        COMMIT TRANSACTION;
    END TRY
    BEGIN CATCH
        IF XACT_STATE() <> 0 ROLLBACK T
RANSACTION;
        SET @NewCollectionID = 0;
        SET @Message = 'The collection could not be saved.';
        RETURN;
    END CATCH

    SET @Message = 'Collection saved.';
END


Text
---------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------

CREATE   PROCEDURE dbo.APS_CollectionColumn_Get
    @CollectionID int
AS
BEGIN
    SET NOCOUNT ON;
    SELECT CollectionColumnID, CollectionID, ColumnName, DataTypeName, ColumnLength,
           NumericPrecision, NumericScale, IsNullable, ConstraintType,
 DefaultValue,
           CheckExpression, RefTableName, RefColumnName, IsIdentityColumn,
           IsSystemColumn, SortOrder
    FROM   dbo.APS_CollectionColumn
    WHERE  CollectionID = @CollectionID AND IsActive = 1
    ORDER BY SortOrder;
END


Text
---------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------

/* Header values are never returned here. The UI renders a mask and calls
   APS_ConfigHeader_GetSecret only when the user explicitly reveals one. */
CREATE   PROCEDURE dbo.APS_ConfigHeader_Get
    @cid smallint
AS
BEGIN
    SET NOCOUNT ON;
    SELECT ch
id, headerkey,
           CASE WHEN headervalue IS NULL OR headervalue = '' THEN ''
                ELSE REPLICATE('*', 10) END AS maskedvalue,
           sortorder
    FROM   dbo.APS_ConfigHeader
    WHERE  cid = @cid
    ORDER BY sortorder, chid;
END


Text
---------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------

CREATE   PROCEDURE dbo.APS_ConfigHeader_GetSecret
    @chid smallint
AS
BEGIN
    SET NOCOUNT ON;
    SELECT chid, headerkey, headervalue
    FROM   dbo.APS_ConfigHeader
    WHERE  chid = @chid;
END


Text
---------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------

CREATE   PROCEDURE dbo.APS_ConfigHistory_Get
    @UseCaseID int = NULL,
    @Top       int = 10
AS
BEGIN
    SET NOCOUNT ON;
    SELECT TOP (@Top) h.ConfigHistoryID, h.UseCaseID, u.UseCaseCode,
           h.ConfigItem, h.Detail, h.CreatedBy, h.CreatedDat
e
    FROM   dbo.APS_ConfigHistory h
    LEFT   JOIN dbo.APS_UseCaseMaster u ON u.UseCaseID = h.UseCaseID
    WHERE  (@UseCaseID IS NULL OR h.UseCaseID = @UseCaseID)
    ORDER BY h.ConfigHistoryID DESC;
END


Text
---------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------

CREATE   PROCEDURE dbo.APS_ConfigHistory_Insert
    @UseCaseID  int = NULL,
    @ConfigItem varchar(100),
    @Detail     varchar(1000) = NULL,
    @ActionBy   varchar(30)
AS
BEGIN
    SET NOCOUNT ON;
    INSERT INTO dbo.APS_ConfigHistory (UseCaseID, Con
figItem, Detail, CreatedBy)
    VALUES (@UseCaseID, @ConfigItem, @Detail, @ActionBy);
END


Text
---------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------

CREATE   PROCEDURE dbo.APS_Configuration_SetApiStatus
    @cid       smallint,
    @apistatus varchar(20)
AS
BEGIN
    SET NOCOUNT ON;
    UPDATE dbo.APS_Configuration SET apistatus = @apistatus WHERE cid = @cid;
END


Text
---------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------

/* =============================================================================
   7. INBOX DETAIL LAYOUT
   ============================================================================= */
CREATE   PROCEDURE dbo.APS_DetailConfig_Get
AS
BEGIN
    SET NO
COUNT ON;
    SELECT TOP 1 dcid, mode, knowledgeai, allowapprove, allowreject,
           allowclarify, allowcomments, modifiedby, modifiedon
    FROM   dbo.APS_DetailConfig
    ORDER BY dcid DESC;
END


Text
---------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------

/* Replaces the whole detail layout in one transaction. rowkey/parentrowkey in
   the TVPs are client-side identifiers; they are mapped to real dtid values
   here so the caller never has to know database keys. */
CREATE   PROCEDURE [dbo].[APS_DetailConf
ig_Save]
    @mode          tinyint,
    @knowledgeai   bit,
    @allowapprove  bit,
    @allowreject   bit,
    @allowclarify  bit,
    @allowcomments bit,
    @tabs          dbo.APS_DetailTabType   READONLY,
    @fields        dbo.APS_DetailFieldType RE
ADONLY,
    @actionby      varchar(20),
    @success       bit OUTPUT,
    @message       nvarchar(400) OUTPUT
AS
BEGIN
    SET NOCOUNT ON;
    SET @success = 0;

    IF NOT EXISTS (SELECT 1 FROM @fields WHERE rowkey IS NULL AND mode = @mode)
    BEGIN
  
      SET @message = 'Configure at least one header field.';
        RETURN;
    END
    IF NOT EXISTS (SELECT 1 FROM @tabs WHERE parentrowkey IS NULL AND mode = @mode)
    BEGIN
        SET @message = 'Configure at least one primary tab.';
        RETURN
;
    END
    IF EXISTS (SELECT 1 FROM @tabs WHERE label IS NULL OR LTRIM(RTRIM(label)) = '')
    BEGIN
        SET @message = 'Every tab and sub-tab needs a label.';
        RETURN;
    END
    /* A tab with no sub-tabs must carry its own fields. */
    
IF EXISTS (SELECT 1 FROM @tabs t
               WHERE NOT EXISTS (SELECT 1 FROM @tabs s WHERE s.parentrowkey = t.rowkey)
                 AND NOT EXISTS (SELECT 1 FROM @fields f WHERE f.rowkey = t.rowkey))
    BEGIN
        SET @message = 'Every tab and s
ub-tab must have at least one assigned field.';
        RETURN;
    END

    BEGIN TRY
        BEGIN TRANSACTION;

        DECLARE @dcid smallint = (SELECT TOP 1 dcid FROM dbo.APS_DetailConfig ORDER BY dcid DESC);

        IF @dcid IS NULL
        BEGIN
 
           INSERT INTO dbo.APS_DetailConfig (mode, knowledgeai, allowapprove, allowreject,
                                              allowclarify, allowcomments, modifiedby, modifiedon)
            VALUES (@mode, @knowledgeai, @allowapprove, @allowrej
ect,
                    @allowclarify, @allowcomments, @actionby, GETDATE());
            SET @dcid = CAST(SCOPE_IDENTITY() AS smallint);
        END
        ELSE
        BEGIN
            UPDATE dbo.APS_DetailConfig
            SET    mode = @mode, know
ledgeai = @knowledgeai, allowapprove = @allowapprove,
                   allowreject = @allowreject, allowclarify = @allowclarify,
                   allowcomments = @allowcomments, modifiedby = @actionby, modifiedon = GETDATE()
            WHERE  dcid = 
@dcid;
        END

        /* Scoped to the submitted mode only. The other workspace layout stays in
           place, so switching between Data Review and Evidence Review and saving
           does not discard the layout you are not looking at. */
     
   DELETE FROM dbo.APS_DetailField WHERE dcid = @dcid AND mode = @mode;
        DELETE FROM dbo.APS_DetailTab   WHERE dcid = @dcid AND mode = @mode AND parenttabid IS NOT NULL;
        DELETE FROM dbo.APS_DetailTab   WHERE dcid = @dcid AND mode = @mode;


        -- Changed smallint PRIMARY KEY datatype to VARCHAR(MAX)
        --DECLARE @map TABLE (rowkey smallint PRIMARY KEY, dtid smallint);
        DECLARE @map TABLE (rowkey VARCHAR(MAX), dtid smallint);

        /* Primary tabs first so sub-tabs can res
olve their parent. */
        INSERT INTO dbo.APS_DetailTab (dcid, parenttabid, mode, tabkey, label, icon, displaytype, badge, sortorder)
        OUTPUT inserted.dtid, inserted.tabkey INTO @map (dtid, rowkey)
        SELECT @dcid, NULL, t.mode, t.tabkey, 
t.label, t.icon, t.displaytype, t.badge, t.sortorder
        FROM   @tabs t WHERE t.parentrowkey IS NULL;

        /* OUTPUT cannot read the TVP rowkey directly, so re-map by tabkey. */
        DELETE FROM @map;
        INSERT INTO @map (rowkey, dtid)
   
     SELECT t.rowkey, d.dtid
        FROM   @tabs t
        JOIN   dbo.APS_DetailTab d ON d.dcid = @dcid AND d.mode = t.mode
                                  AND d.tabkey = t.tabkey AND d.parenttabid IS NULL
        WHERE  t.parentrowkey IS NULL;

      
  INSERT INTO dbo.APS_DetailTab (dcid, parenttabid, mode, tabkey, label, icon, displaytype, badge, sortorder)
        SELECT @dcid, m.dtid, t.mode, t.tabkey, t.label, t.icon, t.displaytype, t.badge, t.sortorder
        FROM   @tabs t
        JOIN   @map m
 ON m.rowkey = t.parentrowkey
        WHERE  t.parentrowkey IS NOT NULL;

        INSERT INTO @map (rowkey, dtid)
        SELECT t.rowkey, d.dtid
        FROM   @tabs t
        JOIN   @map p ON p.rowkey = t.parentrowkey
        JOIN   dbo.APS_DetailTab d 
ON d.dcid = @dcid AND d.mode = t.mode
                                  AND d.tabkey = t.tabkey AND d.parenttabid = p.dtid
        WHERE  t.parentrowkey IS NOT NULL
          AND  NOT EXISTS (SELECT 1 FROM @map x WHERE x.rowkey = t.rowkey);

        INSER
T INTO dbo.APS_DetailField (dcid, dtid, mode, fieldname, isrequired, sortorder)
        SELECT @dcid, m.dtid, f.mode, f.fieldname, f.isrequired, f.sortorder
        FROM   @fields f
        LEFT JOIN @map m ON m.rowkey = f.rowkey;

        INSERT INTO dbo
.APS_Configuration_History (configitem, detail, actionby, actiondt)
        VALUES ('Detail layout',
                CASE WHEN @mode = 1 THEN 'Data Review Workspace' ELSE 'Evidence Review Workspace' END
                + ' - ' + CAST((SELECT COUNT(*) FROM
 @tabs WHERE parentrowkey IS NULL) AS varchar(10))
                + ' primary tabs', @actionby, GETDATE());

        COMMIT TRANSACTION;
        SET @success = 1;
        SET @message = 'Detail layout saved.';
    END TRY
    BEGIN CATCH
        IF @@TRA
NCOUNT > 0 ROLLBACK TRANSACTION;
        SET @message = ERROR_MESSAGE();
    END CATCH
END


Text
---------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------

CREATE   PROCEDURE dbo.APS_DetailField_Get
    @dcid smallint
AS
BEGIN
    SET NOCOUNT ON;
    SELECT dfid, dcid, dtid, mode, fieldname, isrequired, sortorder
    FROM   dbo.APS_DetailField
    WHERE  dcid = @dcid
    ORDER BY mode, dtid, sortorder, dfid
;
END


Text
---------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------
CREATE PROCEDURE APS_DetailTab_Delete
(
    @dtid INT
)
AS
BEGIN
    SET NOCOUNT ON;

    BEGIN TRY
        BEGIN TRANSACTION;

        -- Delete child records first
        DELETE FROM APS_DetailField
        WHERE dtid = @dtid;

        -- Delete parent record
        DELETE FROM APS_DetailTab
        WHERE dtid = @dtid;

        COMMIT TRANSACTION;
    END TRY
    BEGIN CATCH
        IF @@TRANCOUNT > 0
            ROLLBACK TRANSACTION;

        THROW;
    END CATCH
END;

Text
---------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------

CREATE   PROCEDURE dbo.APS_DetailTab_Get
    @dcid smallint
AS
BEGIN
    SET NOCOUNT ON;
    SELECT dtid, dcid, parenttabid, mode, tabkey, label, icon, displaytype, badge, sortorder
    FROM   dbo.APS_DetailTab
    WHERE  dcid = @dcid
    ORDER BY mode, 
ISNULL(parenttabid, dtid), parenttabid, sortorder, dtid;
END


Text
---------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------

/* =============================================================================
   INBOX SETUP
   ============================================================================= */

CREATE   PROCEDURE [dbo].[APS_InboxConfig_Get]
    @UseCaseID int=1
AS
BEGIN
    SET NOCOUNT ON;

    SELECT InboxConfigID, UseCaseID, GridType, StatusProcedureName
    FROM   dbo.APS_InboxConfig
    WHERE  
    UseCaseID = @UseCaseID AND IsActive = 1;

    SELECT c.InboxConfigID, c.GridType, col.InboxColumnID, col.FieldCode,
           col.DisplayName, col.SortOrder
    FROM   dbo.APS_InboxColumn col
    INNER  JOIN dbo.APS_InboxConfig c ON c.InboxConfigID = col.InboxConfigID
    WHERE  c.UseCaseID = @UseCaseID AND c.IsActive = 1 AND col.IsActive = 1
    ORDER BY c.GridType, col.SortOrder;
END

Text
---------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------

CREATE   PROCEDURE dbo.APS_InboxConfig_Save
    @UseCaseID           int,
    @GridType            tinyint,
    @StatusProcedureName varchar(128) = NULL,
    @Columns             dbo.APS_InboxColumnType READONLY,
    @ActionBy            varchar(30),
   
 @Success             tinyint OUTPUT,
    @Message             varchar(200) OUTPUT
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;
    SET @Success = 0;

    IF NOT EXISTS (SELECT 1 FROM @Columns)
    BEGIN
        SET @Message = 'Select at least one 
column for this grid.';
        RETURN;
    END

    -- A status grid runs a procedure by name, so the name is checked against
    -- sys.procedures rather than trusted. A screen must not be able to nominate
    -- an object that does not exist, or one ou
tside the APS_ family.
    IF @GridType = 2
    BEGIN
        IF LEN(LTRIM(RTRIM(ISNULL(@StatusProcedureName, '')))) = 0
        BEGIN
            SET @Message = 'Enter the stored procedure name for the status grid.';
            RETURN;
        END

    
    IF LEFT(@StatusProcedureName, 6) <> 'APS_'
        BEGIN
            SET @Message = 'The status procedure name must start with APS_.';
            RETURN;
        END

        IF NOT EXISTS (SELECT 1 FROM sys.procedures WHERE name = @StatusProcedureNa
me)
        BEGIN
            SET @Message = 'That stored procedure does not exist in this database.';
            RETURN;
        END
    END

    DECLARE @configId int;

    BEGIN TRY
        BEGIN TRANSACTION;

        SELECT @configId = InboxConfigID

        FROM   dbo.APS_InboxConfig
        WHERE  UseCaseID = @UseCaseID AND GridType = @GridType;

        IF @configId IS NULL
        BEGIN
            INSERT INTO dbo.APS_InboxConfig (UseCaseID, GridType, StatusProcedureName, CreatedBy)
            VA
LUES (@UseCaseID, @GridType, @StatusProcedureName, @ActionBy);

            SET @configId = CAST(SCOPE_IDENTITY() AS int);
        END
        ELSE
        BEGIN
            UPDATE dbo.APS_InboxConfig
               SET StatusProcedureName = @StatusProced
ureName, IsActive = 1,
                   ModifiedBy = @ActionBy, ModifiedDate = GETDATE()
             WHERE InboxConfigID = @configId;
        END

        DELETE FROM dbo.APS_InboxColumn WHERE InboxConfigID = @configId;

        INSERT INTO dbo.APS_Inb
oxColumn (InboxConfigID, FieldCode, DisplayName, SortOrder, CreatedBy)
        SELECT @configId, FieldCode, DisplayName, SortOrder, @ActionBy
        FROM   @Columns;

        EXEC dbo.APS_ConfigHistory_Insert
             @UseCaseID = @UseCaseID,
       
      @ConfigItem = 'Inbox layout',
             @Detail = 'Grid type ',
             @ActionBy = @ActionBy;

        COMMIT TRANSACTION;
    END TRY
    BEGIN CATCH
        IF XACT_STATE() <> 0 ROLLBACK TRANSACTION;
        SET @Message = 'The inbox layo
ut could not be saved.';
        RETURN;
    END CATCH

    SET @Success = 1;
    SET @Message = 'Inbox layout saved.';
END


Text
---------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------

/* --- Inbox grid 2 fallback -------------------------------------------------
   Used when an Admin has not nominated a status procedure yet, so the second
   grid shows something real instead of an error. Its signature is the contract
   a custom proce
dure must match: @UseCaseID and @UserName. */
CREATE   PROCEDURE dbo.APS_InboxStatus_Default
    @UseCaseID int,
    @UserName  varchar(50) = NULL
AS
BEGIN
    SET NOCOUNT ON;
    SELECT InflowDataID,
           RowNumber,
           ReviewStatus     = Re
viewStatusCode,
           ProcessingStatus = ProcessingStatusCode,
           AssignedTo       = ISNULL(AssignedTo, 'Unassigned'),
           UpdatedOn        = ISNULL(ModifiedDate, CreatedDate)
    FROM   dbo.APS_InflowData
    WHERE  UseCaseID = @UseCa
seID
      AND  IsActive = 1
      AND (@UserName IS NULL OR AssignedTo IS NULL OR AssignedTo = @UserName)
    ORDER BY InflowDataID DESC;
END


Text
---------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------

CREATE   PROCEDURE dbo.APS_Inflow_Finalize
    @InflowID        int,
    @ValidRowCount   int,
    @InvalidRowCount int,
    @StatusCode      varchar(50),
    @ActionBy        varchar(30)
AS
BEGIN
    SET NOCOUNT ON;
    UPDATE dbo.APS_Inflow
       SET 
ValidRowCount = @ValidRowCount,
           InvalidRowCount = @InvalidRowCount,
           StatusCode = @StatusCode,
           ModifiedBy = @ActionBy,
           ModifiedDate = GETDATE()
     WHERE InflowID = @InflowID;
END


Text
---------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------

/* -----------------------------------------------------------------------------
   One batch, for the Inflow history drill-down.
   ----------------------------------------------------------------------------- */
CREATE   PROCEDURE dbo.APS_Inflow_GetByI
d
    @InflowID int
AS
BEGIN
    SET NOCOUNT ON;

    SELECT i.InflowID, i.UseCaseID, u.UseCaseCode, i.InflowConfigID, f.VersionNo,
           i.OriginalFileName, i.UploadedBy, i.UploadedOn,
           i.TotalRowCount, i.ValidRowCount, i.InvalidRowCount, 
i.StatusCode
    FROM   dbo.APS_Inflow i
    INNER  JOIN dbo.APS_UseCaseMaster u ON u.UseCaseID = i.UseCaseID
    INNER  JOIN dbo.APS_InflowConfig  f ON f.InflowConfigID = i.InflowConfigID
    WHERE  i.InflowID = @InflowID;

    /* Rejected rows only. A c
lean import returns nothing here, which is the
       point: the screen shows a problem list, not a data dump. */
    SELECT TOP (200) InflowDataID, RowNumber, ValidationMessage
    FROM   dbo.APS_InflowData
    WHERE  InflowID = @InflowID AND IsEligible 
= 0
    ORDER BY RowNumber;
END


Text
---------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------

CREATE   PROCEDURE dbo.APS_Inflow_GetHistory
    @UseCaseID int = NULL,
    @Top       int = 10
AS
BEGIN
    SET NOCOUNT ON;
    SELECT TOP (@Top)
           i.InflowID, i.UseCaseID, u.UseCaseCode, i.InflowConfigID, f.VersionNo,
           i.OriginalFile
Name, i.UploadedBy, i.UploadedOn,
           i.TotalRowCount, i.ValidRowCount, i.InvalidRowCount, i.StatusCode
    FROM   dbo.APS_Inflow i
    INNER  JOIN dbo.APS_UseCaseMaster u ON u.UseCaseID = i.UseCaseID
    INNER  JOIN dbo.APS_InflowConfig  f ON f.In
flowConfigID = i.InflowConfigID
    WHERE  (@UseCaseID IS NULL OR i.UseCaseID = @UseCaseID)
    ORDER BY i.InflowID DESC;
END


Text
---------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------

/* =============================================================================
   RUNTIME INFLOW
   ============================================================================= */

CREATE   PROCEDURE dbo.APS_Inflow_Insert
    @UseCaseID        int,
  
  @InflowConfigID   int,
    @OriginalFileName varchar(260),
    @StoredFileName   varchar(260) = NULL,
    @TotalRowCount    int,
    @UploadedBy       varchar(30),
    @InflowID         int OUTPUT,
    @Message          varchar(200) OUTPUT
AS
BEGIN
    
SET NOCOUNT ON;
    SET @InflowID = 0;

    IF NOT EXISTS (SELECT 1 FROM dbo.APS_InflowConfig
                   WHERE InflowConfigID = @InflowConfigID
                     AND UseCaseID = @UseCaseID
                     AND IsActiveTemplate = 1 AND IsAct
ive = 1)
    BEGIN
        SET @Message = 'The active template changed while the file was being read. Reload and try again.';
        RETURN;
    END

    INSERT INTO dbo.APS_Inflow
        (UseCaseID, InflowConfigID, OriginalFileName, StoredFileName,
   
      UploadedBy, TotalRowCount, StatusCode, CreatedBy)
    VALUES
        (@UseCaseID, @InflowConfigID, @OriginalFileName, @StoredFileName,
         @UploadedBy, @TotalRowCount, 'Importing', @UploadedBy);

    SET @InflowID = CAST(SCOPE_IDENTITY() AS int
);
    SET @Message = 'Batch created.';
END


Text
---------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------

/* -----------------------------------------------------------------------------
   Save a whole upload in one transaction.

   The batch header, its rows and the final counts are written together, so a
   failure part-way leaves nothing behind. Writing 
the header first and the rows
   afterwards is what produces a batch claiming fifty records with none stored.
   ----------------------------------------------------------------------------- */
CREATE   PROCEDURE dbo.APS_Inflow_Save
    @UseCaseID        
int,
    @InflowConfigID   int,
    @OriginalFileName varchar(260),
    @StoredFileName   varchar(260) = NULL,
    @TotalRowCount    int,
    @ValidRowCount    int,
    @InvalidRowCount  int,
    @Rows             dbo.APS_InflowDataType READONLY,
    @Upl
oadedBy       varchar(30),
    @InflowID         int OUTPUT,
    @Message          varchar(200) OUTPUT
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;
    SET @InflowID = 0;

    IF NOT EXISTS (SELECT 1 FROM @Rows)
    BEGIN
        SET @Message = 'Th
e workbook has no data rows.';
        RETURN;
    END

    /* The template may have been replaced while the file was being read. The
       batch must record the version it was actually validated against. */
    IF NOT EXISTS (SELECT 1 FROM dbo.APS_Inflo
wConfig
                   WHERE InflowConfigID = @InflowConfigID
                     AND UseCaseID = @UseCaseID
                     AND IsActiveTemplate = 1 AND IsActive = 1)
    BEGIN
        SET @Message = 'The active template changed while the file 
was being read. Reload the page and upload again.';
        RETURN;
    END

    BEGIN TRY
        BEGIN TRANSACTION;

        INSERT INTO dbo.APS_Inflow
            (UseCaseID, InflowConfigID, OriginalFileName, StoredFileName, UploadedBy,
             To
talRowCount, ValidRowCount, InvalidRowCount, StatusCode, CreatedBy)
        VALUES
            (@UseCaseID, @InflowConfigID, @OriginalFileName, @StoredFileName, @UploadedBy,
             @TotalRowCount, @ValidRowCount, @InvalidRowCount,
             CASE 
WHEN @InvalidRowCount = 0 THEN 'Imported' ELSE 'Imported with errors' END,
             @UploadedBy);

        SET @InflowID = CAST(SCOPE_IDENTITY() AS int);

        INSERT INTO dbo.APS_InflowData
            (InflowID, UseCaseID, RowNumber, RowDataJson,
 ValidationStatusCode,
             ValidationMessage, IsEligible, ProcessingStatusCode, ReviewStatusCode, CreatedBy)
        SELECT @InflowID, @UseCaseID, RowNumber, RowDataJson, ValidationStatusCode,
               ValidationMessage, IsEligible, 'Pendin
g',
               /* An invalid row is not put in front of a reviewer as if it were
                  ready. It lands in its own bucket instead. */
               CASE WHEN IsEligible = 1 THEN 'Pending Review' ELSE 'Validation Failed' END,
              
 @UploadedBy
        FROM   @Rows;

        COMMIT TRANSACTION;
    END TRY
    BEGIN CATCH
        IF XACT_STATE() <> 0 ROLLBACK TRANSACTION;
        SET @InflowID = 0;
        SET @Message = 'The upload could not be saved. Nothing was written.';
       
 RETURN;
    END CATCH

    SET @Message = CAST(@ValidRowCount AS varchar(10)) + ' row(s) imported'
                 + CASE WHEN @InvalidRowCount > 0
                        THEN ', ' + CAST(@InvalidRowCount AS varchar(10)) + ' rejected by validation.'
  
                      ELSE '.' END;
END


Text
---------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------

/* --- The columns of one specific version, for the version comparison -------- */
CREATE   PROCEDURE dbo.APS_InflowColumnConfig_GetByConfig
    @InflowConfigID int
AS
BEGIN
    SET NOCOUNT ON;
    SELECT ColumnConfigID, InflowConfigID, ExcelColumnName, 
FieldCode, DataType,
           ColumnLength, IsRequired, ShowInInbox, ShowInDetail, SortOrder
    FROM   dbo.APS_InflowColumnConfig
    WHERE  InflowConfigID = @InflowConfigID AND IsActive = 1
    ORDER BY SortOrder;
END


Text
---------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------

/* =============================================================================
   INFLOW TEMPLATE CONFIGURATION
   ============================================================================= */

/* --- The active template for a use case, plus its col
umns ------------------
   Returns two result sets: the config row, then its columns. */
CREATE   PROCEDURE dbo.APS_InflowConfig_GetActive
    @UseCaseID int
AS
BEGIN
    SET NOCOUNT ON;

    DECLARE @configId int =
        (SELECT TOP 1 InflowConfigID
  
       FROM   dbo.APS_InflowConfig
         WHERE  UseCaseID = @UseCaseID AND IsActiveTemplate = 1 AND IsActive = 1
         ORDER BY VersionNo DESC);

    SELECT f.InflowConfigID, f.UseCaseID, f.VersionNo, f.InflowSourceType, f.FileType,
           f.Tem
plateName, f.TemplateFileName, f.TemplateHash, f.ColumnCount,
           f.IsAcknowledged, f.IsActiveTemplate, f.ActivatedOn, f.CreatedBy, f.CreatedDate,
           u.UseCaseCode, u.UseCaseName, u.ClientID, u.ProcessID
    FROM   dbo.APS_InflowConfig f
  
  INNER  JOIN dbo.APS_UseCaseMaster u ON u.UseCaseID = f.UseCaseID
    WHERE  f.InflowConfigID = @configId;

    SELECT ColumnConfigID, InflowConfigID, ExcelColumnName, FieldCode, DataType,
           ColumnLength, IsRequired, ShowInInbox, ShowInDetail, S
ortOrder
    FROM   dbo.APS_InflowColumnConfig
    WHERE  InflowConfigID = @configId AND IsActive = 1
    ORDER BY SortOrder;
END


Text
---------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------

/* --- Every version a use case has had, newest first ------------------------ */
CREATE   PROCEDURE dbo.APS_InflowConfig_GetVersions
    @UseCaseID int
AS
BEGIN
    SET NOCOUNT ON;
    SELECT InflowConfigID, VersionNo, TemplateName, TemplateFileName, Fi
leType,
           ColumnCount, IsActiveTemplate, ActivatedOn, DeactivatedOn,
           CreatedBy, CreatedDate
    FROM   dbo.APS_InflowConfig
    WHERE  UseCaseID = @UseCaseID AND IsActive = 1
    ORDER BY VersionNo DESC;
END


Text
---------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------

/* --- Save a template version ----------------------------------------------
   One active template per use case. Saving deactivates the current one and
   inserts the next version number in a single transaction, so there is never a
   moment where a us
e case has two active templates or none.

   @ReplaceExisting guards the confirmation step: the first save of a use case
   that already has a template returns AlreadyExists = 1 and writes nothing, so
   the screen can ask before overwriting. */
CREATE   
PROCEDURE dbo.APS_InflowConfig_Save
    @UseCaseID        int,
    @InflowSourceType tinyint,
    @FileType         varchar(20),
    @TemplateName     varchar(200),
    @TemplateFileName varchar(260),
    @TemplateHash     char(64),
    @IsAcknowledged   
tinyint,
    @ReplaceExisting  tinyint,
    @Columns          dbo.APS_InflowColumnType READONLY,
    @ActionBy         varchar(30),
    @NewInflowConfigID int OUTPUT,
    @AlreadyExists    tinyint OUTPUT,
    @Message          varchar(200) OUTPUT
AS
BEGIN

    SET NOCOUNT ON;
    SET XACT_ABORT ON;

    SET @NewInflowConfigID = 0;
    SET @AlreadyExists = 0;

    IF @IsAcknowledged = 0
    BEGIN
        SET @Message = 'Tick the confirmation before saving the configuration.';
        RETURN;
    END

    IF
 NOT EXISTS (SELECT 1 FROM @Columns)
    BEGIN
        SET @Message = 'Apply the template columns before saving.';
        RETURN;
    END

    IF NOT EXISTS (SELECT 1 FROM dbo.APS_UseCaseMaster WHERE UseCaseID = @UseCaseID AND IsActive = 1)
    BEGIN
   
     SET @Message = 'Select an active client and process.';
        RETURN;
    END

    DECLARE @currentId int, @currentVersion int = 0, @currentHash char(64);

    SELECT TOP 1 @currentId = InflowConfigID, @currentVersion = VersionNo, @currentHash = Tem
plateHash
    FROM   dbo.APS_InflowConfig
    WHERE  UseCaseID = @UseCaseID AND IsActiveTemplate = 1 AND IsActive = 1
    ORDER BY VersionNo DESC;

    -- An identical template is not a new version.
    IF @currentId IS NOT NULL AND @currentHash = @Templa
teHash
    BEGIN
        SET @NewInflowConfigID = @currentId;
        SET @Message = 'This template is identical to the active version. Nothing was changed.';
        RETURN;
    END

    IF @currentId IS NOT NULL AND @ReplaceExisting = 0
    BEGIN
      
  SET @AlreadyExists = 1;
        SET @Message = 'An active template already exists for this use case. Replace it?';
        RETURN;
    END

    DECLARE @nextVersion int =
        ISNULL((SELECT MAX(VersionNo) FROM dbo.APS_InflowConfig WHERE UseCaseID = 
@UseCaseID), 0) + 1;

    BEGIN TRY
        BEGIN TRANSACTION;

        IF @currentId IS NOT NULL
            UPDATE dbo.APS_InflowConfig
               SET IsActiveTemplate = 0, DeactivatedOn = GETDATE(),
                   ModifiedBy = @ActionBy, Modifi
edDate = GETDATE()
             WHERE InflowConfigID = @currentId;

        INSERT INTO dbo.APS_InflowConfig
            (UseCaseID, VersionNo, InflowSourceType, FileType, TemplateName, TemplateFileName,
             TemplateHash, ColumnCount, IsAcknowled
ged, IsActiveTemplate, ActivatedOn, CreatedBy)
        VALUES
            (@UseCaseID, @nextVersion, @InflowSourceType, @FileType, @TemplateName, @TemplateFileName,
             @TemplateHash, (SELECT COUNT(*) FROM @Columns), @IsAcknowledged, 1, GETDATE()
, @ActionBy);

        SET @NewInflowConfigID = CAST(SCOPE_IDENTITY() AS int);

        INSERT INTO dbo.APS_InflowColumnConfig
            (InflowConfigID, UseCaseID, ExcelColumnName, FieldCode, DataType, ColumnLength,
             IsRequired, ShowInInbox
, ShowInDetail, SortOrder, CreatedBy)
        SELECT @NewInflowConfigID, @UseCaseID, ExcelColumnName, FieldCode, DataType, ColumnLength,
               IsRequired, ShowInInbox, ShowInDetail, SortOrder, @ActionBy
        FROM   @Columns;

        EXEC dbo.
APS_ConfigHistory_Insert
             @UseCaseID = @UseCaseID,
             @ConfigItem = 'Inflow template',
             @Detail = @TemplateFileName,
             @ActionBy = @ActionBy;

        COMMIT TRANSACTION;
    END TRY
    BEGIN CATCH
        IF 
XACT_STATE() <> 0 ROLLBACK TRANSACTION;
        SET @NewInflowConfigID = 0;
        SET @Message = 'The template could not be saved.';
        RETURN;
    END CATCH

    SET @Message = 'Template version ' + CAST(@nextVersion AS varchar(10)) + ' is now act
ive.';
END


Text
---------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------

/* -----------------------------------------------------------------------------
   5. Archive the live rows before a rebuild

   Called by the controller immediately before APS_InflowData_Rebuild. Built
   dynamically because APS_InflowData has no fixed
 column list - its shape comes
   from whichever template is active. FOR JSON AUTO turns whatever those columns
   happen to be into one payload per row.
   ----------------------------------------------------------------------------- */
CREATE   PROCEDUR
E dbo.APS_InflowData_Archive
    @cid       smallint,
    @actionby  varchar(20),
    @rowcount  int OUTPUT
AS
BEGIN
    SET NOCOUNT ON;
    SET @rowcount = 0;

    IF OBJECT_ID('dbo.APS_InflowData', 'U') IS NULL RETURN;

    IF NOT EXISTS (SELECT 1 FROM 
dbo.APS_InflowData) RETURN;

    DECLARE @sql nvarchar(MAX) = N'
        INSERT INTO dbo.APS_InflowDataArchive (ifdid, ifid, cid, rowjson, archivedby)
        SELECT d.ifdid, d.ifid, @cid,
               (SELECT x.* FROM dbo.APS_InflowData x
             
   WHERE x.ifdid = d.ifdid FOR JSON AUTO, WITHOUT_ARRAY_WRAPPER),
               @actionby
        FROM   dbo.APS_InflowData d;';

    BEGIN TRY
        EXEC sp_executesql @sql,
             N'@cid smallint, @actionby varchar(20)',
             @cid = @ci
d, @actionby = @actionby;

        SET @rowcount = @@ROWCOUNT;
    END TRY
    BEGIN CATCH
        /* Archiving must never block a template change, but the caller needs to
           know it did not happen. Zero means nothing was archived. */
        SET 
@rowcount = -1;
    END CATCH
END


Text
---------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------

/* --- Inbox grid 1: the uploaded rows ---------------------------------------
   RowDataJson is returned whole. The controller reads the configured fields out
   of it, which keeps this procedure independent of any template. */
CREATE   PROCEDURE dbo.AP
S_InflowData_Get
    @UseCaseID    int,
    @ReviewStatus varchar(50) = NULL,
    @Top          int = 500
AS
BEGIN
    SET NOCOUNT ON;
    SELECT TOP (@Top)
           d.InflowDataID, d.InflowID, d.UseCaseID, d.RowNumber, d.RowDataJson,
           d.Valid
ationStatusCode, d.ValidationMessage, d.IsEligible,
           d.ProcessingStatusCode, d.ReviewStatusCode, d.AssignedTo, d.CreatedDate
    FROM   dbo.APS_InflowData d
    WHERE  d.UseCaseID = @UseCaseID
      AND  d.IsActive = 1
      AND (@ReviewStatus I
S NULL OR d.ReviewStatusCode = @ReviewStatus)
    ORDER BY d.InflowDataID DESC;
END


Text
---------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------

CREATE   PROCEDURE dbo.APS_InflowData_GetById
    @InflowDataID int
AS
BEGIN
    SET NOCOUNT ON;
    SELECT d.InflowDataID, d.InflowID, d.UseCaseID, u.UseCaseCode, d.RowNumber,
           d.RowDataJson, d.ValidationStatusCode, d.ValidationMessage,
      
     d.IsEligible, d.ProcessingStatusCode, d.ReviewStatusCode, d.AssignedTo,
           d.CreatedBy, d.CreatedDate
    FROM   dbo.APS_InflowData d
    INNER  JOIN dbo.APS_UseCaseMaster u ON u.UseCaseID = d.UseCaseID
    WHERE  d.InflowDataID = @InflowData
ID;
END


Text
---------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------

CREATE   PROCEDURE dbo.APS_InflowData_GetKpi
    @UseCaseID int
AS
BEGIN
    SET NOCOUNT ON;
    SELECT ReviewStatusCode, RecordCount = COUNT(*)
    FROM   dbo.APS_InflowData
    WHERE  UseCaseID = @UseCaseID AND IsActive = 1
    GROUP BY ReviewStatusCod
e;
END


Text
---------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------

/* -----------------------------------------------------------------------------
   Record a review decision.

   Only a row still awaiting review can be decided, so two reviewers opening the
   same record cannot both record an outcome.
   -------------
---------------------------------------------------------------- */
CREATE   PROCEDURE dbo.APS_InflowData_SaveReview
    @InflowDataID    int,
    @ReviewStatusCode varchar(50),
    @ReviewComment   varchar(2000) = NULL,
    @ActionBy        varchar(30),

    @Success         tinyint OUTPUT,
    @Message         varchar(200) OUTPUT
AS
BEGIN
    SET NOCOUNT ON;
    SET @Success = 0;

    IF @ReviewStatusCode NOT IN ('Approved', 'Rejected', 'Needs Clarification')
    BEGIN
        SET @Message = 'Unknown rev
iew decision.';
        RETURN;
    END

    IF @ReviewStatusCode IN ('Rejected', 'Needs Clarification')
       AND LEN(LTRIM(RTRIM(ISNULL(@ReviewComment, '')))) = 0
    BEGIN
        SET @Message = 'Add a comment explaining the decision.';
        RETURN
;
    END

    DECLARE @current varchar(50);
    SELECT @current = ReviewStatusCode FROM dbo.APS_InflowData WHERE InflowDataID = @InflowDataID;

    IF @current IS NULL
    BEGIN
        SET @Message = 'That record no longer exists.';
        RETURN;
    
END

    IF @current <> 'Pending Review'
    BEGIN
        SET @Message = 'That record was already decided as ' + @current + '.';
        RETURN;
    END

    UPDATE dbo.APS_InflowData
       SET ReviewStatusCode = @ReviewStatusCode,
           ReviewComm
ent    = @ReviewComment,
           ReviewedBy       = @ActionBy,
           ReviewedOn       = GETDATE(),
           AssignedTo       = @ActionBy,
           ProcessingStatusCode = CASE WHEN @ReviewStatusCode = 'Approved' THEN 'Ready' ELSE 'Held' END,
  
         ModifiedBy       = @ActionBy,
           ModifiedDate     = GETDATE()
     WHERE InflowDataID = @InflowDataID
       AND ReviewStatusCode = 'Pending Review';

    IF @@ROWCOUNT = 0
    BEGIN
        SET @Message = 'Another reviewer decided that r
ecord first. Refresh the Inbox.';
        RETURN;
    END

    SET @Success = 1;
    SET @Message = 'Recorded as ' + @ReviewStatusCode + '.';
END


Text
---------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------

CREATE   PROCEDURE dbo.APS_InflowData_UpdateStatus
    @ifdid        int,
    @reviewstatus varchar(50)
AS
BEGIN
    SET NOCOUNT ON;
    UPDATE dbo.APS_InflowData SET reviewstatus = @reviewstatus WHERE ifdid = @ifdid;
END


Text
---------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------

/* --- read archived rows back ------------------------------------------------ */
CREATE   PROCEDURE dbo.APS_InflowDataArchive_Get
    @cid  smallint = NULL,
    @ifid int = NULL,
    @top  int = 200
AS
BEGIN
    SET NOCOUNT ON;
    SELECT TOP (@top) a.
archiveid, a.ifdid, a.ifid, a.cid, c.versionno,
           a.rowjson, a.archivedon, a.archivedby
    FROM   dbo.APS_InflowDataArchive a
    LEFT   JOIN dbo.APS_Configuration c ON c.cid = a.cid
    WHERE  (@cid IS NULL OR a.cid = @cid)
      AND  (@ifid IS
 NULL OR a.ifid = @ifid)
    ORDER BY a.archiveid DESC;
END


Text
---------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------
CREATE PROCEDURE APS_InflowDataReport_Get
(
    @Company     NVARCHAR(100) = NULL,
    @Date        DATE = NULL,
    @ActionDate  DATE = NULL,
    @Status      NVARCHAR(50) = NULL
)
AS
BEGIN
    SET NOCOUNT ON;

    SELECT *
    FROM APS_InflowData
    WHERE
        (@Company IS NULL OR Company = @Company)
        AND (@Date IS NULL OR [Date] = @Date)
        AND (@ActionDate IS NULL OR Action_date = @ActionDate)
        AND (@Status IS NULL OR Status = @Status);
END;

Text
---------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------
CREATE PROCEDURE APS_ManageCategory_Delete
(
    @CategoryId INT
)
AS
BEGIN
    SET NOCOUNT ON;

    UPDATE APS_ManageCategory SET Active = 0
    WHERE CategoryId = @CategoryId;

    SELECT @@ROWCOUNT AS RowsAffected;
END;

Text
---------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------
CREATE PROCEDURE APS_ManageCategory_Get
AS
BEGIN
    SET NOCOUNT ON;

    SELECT
        CategoryId,
        CategoryName,
        Active,
        CreatedBy,
        CreatedOn
    FROM APS_ManageCategory
    ORDER BY CategoryName;
END;

Text
---------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------
CREATE PROCEDURE APS_ManageCategory_Insert
(
    @CategoryName NVARCHAR(255),
    @Active BIT = 1,
    @CreatedBy NVARCHAR(100)
)
AS
BEGIN
    SET NOCOUNT ON;

    INSERT INTO APS_ManageCategory
    (
        CategoryName,
        Active,
        CreatedBy,
        CreatedOn
    )
    VALUES
    (
        @CategoryName,
        @Active,
        @CreatedBy,
        GETDATE()
    );

    SELECT SCOPE_IDENTITY() AS CategoryId;
END;

Text
---------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------
CREATE PROCEDURE dbo.APS_OutputType_GetAll
AS
BEGIN
    SET NOCOUNT ON;

    SELECT *
    FROM dbo.APS_OutputType
    ORDER BY OutputTypeID;
END


Text
---------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------

CREATE   PROCEDURE dbo.APS_ProcessMaster_Get
    @ActiveOnly tinyint = 1
AS
BEGIN
    SET NOCOUNT ON;
    SELECT ProcessID, ProcessCode, ProcessName, IsActive
    FROM   dbo.APS_ProcessMaster
    WHERE  (@ActiveOnly = 0 OR IsActive = 1)
    ORDER BY Proc
essName;
END


Text
---------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------
CREATE PROCEDURE APS_Report_Configuration_Get  
@reportId INT  
AS  
BEGIN  
   SELECT * FROM APS_ReportMaster WHERE ReportId = @reportId  
   SELECT * FROM APS_ReportParameter WHERE ReportId = @reportId  
   SELECT * FROM ASP_ReportColumns WHERE ReportId = @reportId  
END

Text
---------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------
CREATE PROCEDURE dbo.APS_ReportConfiguration_Get
    @ReportId INT
AS
BEGIN
    SET NOCOUNT ON;

    -- Report Master Details
    SELECT *
    FROM APS_ReportMaster
    WHERE ReportId = @ReportId;

    -- Report Parameter Details
    SELECT *
    FROM APS_ReportParameter
    WHERE ReportId = @ReportId;
END;

Text
---------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------
CREATE PROCEDURE APS_ReportMaster_Get
--@reportId INT,
--@pageNumber INT,
--@pageSize INT
AS
BEGIN
   SELECT * FROM APS_ReportMaster --WHERE ReportId = @reportId
END

Text
---------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------
CREATE PROCEDURE dbo.APS_ReportMaster_Insert
(
      @ReportName           VARCHAR(200)
    , @ReportCode           VARCHAR(200)
    , @Description          VARCHAR(MAX) = NULL
    , @ProcedureSchema      VARCHAR(200)
    , @ProcedureName        VARCHAR(100)
    , @ExecutionMode        VARCHAR(100) = NULL
    , @DefaultPageSize      INT = 10
    , @MaxInteractiveRows   INT = 5000
    , @CommandTimeout       INT = 120
    , @IsActive             BIT = 1

    , @ReportId             SMALLINT OUTPUT
    , @Message              NVARCHAR(400) OUTPUT
)
AS
BEGIN
    SET NOCOUNT ON;

    BEGIN TRY

        -- Duplicate Report Code Validation
        IF EXISTS
        (
            SELECT 1
            FROM APS_ReportMaster
            WHERE ReportCode = @ReportCode
        )
        BEGIN
            SET @ReportId = 0;
            SET @Message = 'Report Code already exists.';
            RETURN;
        END

        INSERT INTO APS_ReportMaster
        (
              ReportName
            , ReportCode
            , Description
            , ProcedureSchema
            , ProcedureName
            , ExecutionMode
            , DefaultPageSize
            , MaxInteractiveRows
            , CommandTimeout
            , IsActive
        )
        VALUES
        (
              @ReportName
            , @ReportCode
            , @Description
            , @ProcedureSchema
            , @ProcedureName
            , @ExecutionMode
            , @DefaultPageSize
            , @MaxInteractiveRows
            , @CommandTimeout
            , @IsActive
        );

        SET @ReportId = CAST(SCOPE_IDENTITY() AS SMALLINT);
        SET @Message = 'Report created successfully.';

    END TRY
    BEGIN CATCH

        SET @ReportId = 0;
        SET @Message = ERROR_MESSAGE();

    END CATCH
END

Text
---------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------

CREATE PROCEDURE APS_ReportParameter_Get
AS
BEGIN
   SELECT * FROM APS_ReportParameter
END

Text
---------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------
CREATE PROCEDURE dbo.APS_ReportParameter_Insert  
(  
      @ReportId INT  
    , @ParameterName VARCHAR(100)  
    , @DisplayName VARCHAR(50)  
    , @DataType VARCHAR(50)  
    , @ControlType VARCHAR(50)  
    , @DefaultValue VARCHAR(50)  
    , @IsRequired BIT  
)  
AS  
BEGIN  
    SET NOCOUNT ON;  
  
    INSERT INTO dbo.APS_ReportParameter  
    (  
          ReportId  
        , ParameterName  
        , DisplayName  
        , DataType  
        , ControlType  
        , DefaultValue  
        , IsRequired  
    )  
    VALUES  
    (  
          @ReportId  
        , @ParameterName  
        , @DisplayName  
        , @DataType  
        , @ControlType  
        , @DefaultValue  
        , @IsRequired  
    );  
  
    SELECT  
          CAST(SCOPE_IDENTITY() AS INT) AS ParameterId  
        , 1 AS IsSuccess  
        , 'Parameter saved successfully.' AS Message;  
END 

Text
---------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------

CREATE   PROCEDURE dbo.APS_Review_GetByRecord
    @ifdid int
AS
BEGIN
    SET NOCOUNT ON;
    SELECT rvid, ifdid, decision, comments, reviewedby, reviewedon
    FROM   dbo.APS_Review
    WHERE  ifdid = @ifdid
    ORDER BY rvid DESC;
END


Text
---------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------

/* =============================================================================
   9. HUMAN REVIEW
   ============================================================================= */
CREATE   PROCEDURE dbo.APS_Review_Insert
    @ifdid      int,
    @dec
ision   varchar(50),
    @comments   nvarchar(MAX) = NULL,
    @reviewedby varchar(20),
    @success    bit OUTPUT,
    @message    nvarchar(200) OUTPUT
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;   -- roll back automatically if any statement fail
s
    SET @success = 0;

    IF NOT EXISTS (SELECT 1 FROM dbo.APS_InflowData WHERE ifdid = @ifdid)
    BEGIN
        SET @message = 'Record not found.';
        RETURN;
    END

    BEGIN TRANSACTION;

    INSERT INTO dbo.APS_Review (ifdid, decision, comm
ents, reviewedby, reviewedon)
    VALUES (@ifdid, @decision, @comments, @reviewedby, GETDATE());

    UPDATE dbo.APS_InflowData SET reviewstatus = @decision WHERE ifdid = @ifdid;

    COMMIT TRANSACTION;
    SET @success = 1;
    SET @message = 'Review re
corded as ' + @decision + '.';
END


Text
---------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------
CREATE PROCEDURE [dbo].[APS_ReviewStatus]
(
    @Status      NVARCHAR(50) = NULL
)
AS
BEGIN
    SET NOCOUNT ON;

    SELECT *
    FROM APS_InflowData
    WHERE
        (@Status IS NULL OR reviewstatus = @Status);
END;

Text
---------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------

CREATE   PROCEDURE dbo.APS_RoleMaster_Get
AS
BEGIN
    SET NOCOUNT ON;
    SELECT RoleID, RoleName, IsActive
    FROM   dbo.APS_RoleMaster
    WHERE  IsActive = 1
    ORDER BY RoleID;
END


Text
---------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------
CREATE PROCEDURE dbo.APS_SourceRegistry_Delete
(
    @SourceId INT
)
AS
BEGIN
    SET NOCOUNT ON;

    BEGIN TRY
        -- Check whether record exists
        IF NOT EXISTS (
            SELECT 1
            FROM dbo.APS_SourceRegistry
            WHERE SourceId = @SourceId
        )
        BEGIN
            RAISERROR('Source Registry record not found.', 16, 1);
            RETURN;
        END

        -- Delete record
        UPDATE dbo.APS_SourceRegistry SET Status = 0
        WHERE SourceId = @SourceId;

        -- Success message
        SELECT
            1 AS Success,
            'Record deleted successfully.' AS Message;
    END TRY
    BEGIN CATCH
        SELECT
            0 AS Success,
            ERROR_MESSAGE() AS Message;
    END CATCH
END;

Text
---------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------
CREATE PROCEDURE APS_SourceRegistry_Get     
AS      
BEGIN      
    SET NOCOUNT ON;      
      
    SELECT 
            SourceId, 
            SourceName , 
            SourceCode , 
            SourceType, 
            AccessInterface, 
            OwnerTeam , 
            CASE 
            WHEN Status = 1 THEN 'TRUE' 
            WHEN Status = 0 THEN 'FALSE' END AS Status,
            DataClassification,
            AuthenticationConnection,
            Description,
            DeveloperNotesConfiguration
    FROM dbo.APS_SourceRegistry WITH(NOLOCK) WHERE Status = 1
    ORDER BY SourceID;
END; 

Text
---------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------
CREATE PROCEDURE APS_SourceRegistry_GetByid   
    @SourceId INT = NULL    
AS    
BEGIN    
    SET NOCOUNT ON;    
    
    SELECT *    
    FROM dbo.APS_SourceRegistry    
    WHERE (SourceId IS NULL OR SourceId = @SourceId);    
END; 

Text
---------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------
CREATE PROCEDURE APS_SourceRegistry_Save  
(  
    @SourceId                      INT = NULL,  
    @sourcename                    VARCHAR(255),  
    @sourcecode                    VARCHAR(100),  
    @sourcetype                    VARCHAR(100) = NULL,  
    @accessinterface               VARCHAR(255) = NULL,  
    @ownerteam                     VARCHAR(255) = NULL,  
    @status                        VARCHAR(50) = NULL,  
    @dataclassification            VARCHAR(100) = NULL,  
    @authenticationConnection      VARCHAR(255) = NULL, 
    @description                   VARCHAR(1000) = NULL,
    @developerNotesConfiguration   VARCHAR(2000) = NULL  
)  
AS  
BEGIN  
    SET NOCOUNT ON;  
  
    -- INSERT  
    IF ISNULL(@SourceId, 0) = 0  
    BEGIN  
        IF EXISTS  
        (  
            SELECT 1  
            FROM APS_SourceRegistry  
            WHERE SourceCode = @SourceCode  
        )  
        BEGIN  
            RAISERROR('Source Code already exists.', 16, 1);  
            RETURN;  
        END  
  
        INSERT INTO APS_SourceRegistry  
        (  
            SourceName,  
            SourceCode,  
            SourceType,  
            AccessInterface,  
            OwnerTeam,  
            Status,  
            DataClassification,  
            AuthenticationConnection,  
            Description,  
            DeveloperNotesConfiguration  
        )  
        VALUES  
        (  
            @SourceName,  
            @SourceCode,  
            @SourceType,  
            @AccessInterface,  
            @OwnerTeam,  
            @Status,  
            @DataClassification,  
            @AuthenticationConnection,  
            @Description,  
            @DeveloperNotesConfiguration  
        );  
  
        SELECT SCOPE_IDENTITY() AS SourceId,  
               'Inserted Successfully' AS Message;  
    END  
    ELSE  
    BEGIN  
        -- UPDATE  
  
        IF EXISTS  
        (  
            SELECT 1  
            FROM APS_SourceRegistry  
            WHERE SourceCode = @SourceCode  
              AND SourceId <> @SourceId  
        )  
        BEGIN  
            RAISERROR('Source Code already exists.', 16, 1);  
            RETURN;  
        END  
  
        UPDATE APS_SourceRegistry  
        SET  
            SourceName = @SourceName,  
            SourceCode = @SourceCode,  
            SourceType = @SourceType,  
            AccessInterface = @AccessInterface,  
            OwnerTeam = @OwnerTeam,  
            Status = @Status,  
            DataClassification = @DataClassification,  
            AuthenticationConnection = @AuthenticationConnection,  
            Description = @Description,  
            DeveloperNotesConfiguration = @DeveloperNotesConfiguration  
        WHERE SourceId = @SourceId;  
  
        SELECT @SourceId AS SourceId,  
               'Updated Successfully' AS Message;  
    END  
END; 

Text
---------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------
CREATE PROCEDURE dbo.APS_SourceType_GetAll
AS
BEGIN
    SET NOCOUNT ON;

    SELECT
        SourceTypeID,
        SourceTypeName,
        Description,
        IsActive,
        CreatedDate
    FROM dbo.APS_SourceType
    ORDER BY SourceTypeName;
END


Text
---------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------

/* --- The CAA-SET dropdown -------------------------------------------------
   @UserName restricts the list to the use cases a reviewer is mapped to.
   An Admin passes NULL and sees everything. */
CREATE   PROCEDURE dbo.APS_UseCaseMaster_Get
    @Clie
ntID  int = NULL,
    @ProcessID int = NULL,
    @UserName  varchar(50) = NULL
AS
BEGIN
    SET NOCOUNT ON;

    SELECT u.UseCaseID, u.UseCaseCode, u.UseCaseName,
           u.ClientID, c.ClientCode, c.ClientName,
           u.ProcessID, p.ProcessCode, p.
ProcessName,
           HasActiveTemplate = CASE WHEN EXISTS (
                SELECT 1 FROM dbo.APS_InflowConfig f
                WHERE f.UseCaseID = u.UseCaseID AND f.IsActiveTemplate = 1 AND f.IsActive = 1)
                THEN 1 ELSE 0 END
    FROM  
 dbo.APS_UseCaseMaster u
    INNER  JOIN dbo.APS_ClientMaster  c ON c.ClientID  = u.ClientID
    INNER  JOIN dbo.APS_ProcessMaster p ON p.ProcessID = u.ProcessID
    WHERE  u.IsActive = 1
      AND (@ClientID  IS NULL OR u.ClientID  = @ClientID)
      AND
 (@ProcessID IS NULL OR u.ProcessID = @ProcessID)
      AND (@UserName  IS NULL OR EXISTS (
                SELECT 1
                FROM   dbo.APS_UserUseCaseMapping m
                INNER  JOIN dbo.APS_UserMaster us ON us.UserID = m.UserID
            
    WHERE  m.UseCaseID = u.UseCaseID
                  AND  m.IsActive = 1
                  AND  us.UserName = @UserName))
    ORDER BY c.ClientCode, p.ProcessCode;
END


Text
---------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------

CREATE   PROCEDURE dbo.APS_UseCaseMaster_Save
    @UseCaseID  int,
    @ClientID   int,
    @ProcessID  int,
    @ActionBy   varchar(30),
    @NewUseCaseID int OUTPUT,
    @Message    varchar(200) OUTPUT
AS
BEGIN
    SET NOCOUNT ON;
    SET @NewUseCaseID
 = 0;

    DECLARE @code varchar(50), @name varchar(200);

    SELECT @code = c.ClientCode + '-' + p.ProcessCode,
           @name = c.ClientName + ' - ' + p.ProcessName
    FROM   dbo.APS_ClientMaster c
    CROSS  JOIN dbo.APS_ProcessMaster p
    WHERE  
c.ClientID = @ClientID AND p.ProcessID = @ProcessID;

    IF @code IS NULL
    BEGIN
        SET @Message = 'Select a valid client and process.';
        RETURN;
    END

    IF EXISTS (SELECT 1 FROM dbo.APS_UseCaseMaster
               WHERE UseCaseCode 
= @code AND UseCaseID <> @UseCaseID)
    BEGIN
        SET @Message = 'That client and process combination already exists.';
        RETURN;
    END

    IF @UseCaseID = 0
    BEGIN
        INSERT INTO dbo.APS_UseCaseMaster (ClientID, ProcessID, UseCaseCo
de, UseCaseName, CreatedBy)
        VALUES (@ClientID, @ProcessID, @code, @name, @ActionBy);

        SET @NewUseCaseID = CAST(SCOPE_IDENTITY() AS int);
        SET @Message = 'Use case created.';
        RETURN;
    END

    UPDATE dbo.APS_UseCaseMaster

       SET ClientID = @ClientID, ProcessID = @ProcessID,
           UseCaseCode = @code, UseCaseName = @name,
           ModifiedBy = @ActionBy, ModifiedDate = GETDATE()
     WHERE UseCaseID = @UseCaseID;

    SET @NewUseCaseID = @UseCaseID;
    SET @Mess
age = 'Use case updated.';
END


Text
---------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------

CREATE   PROCEDURE dbo.APS_UserMaster_GetByUserName
    @UserName varchar(50)
AS
BEGIN
    SET NOCOUNT ON;
    SELECT u.UserID, u.UserName, u.FirstName, u.LastName, u.EmailID,
           u.RoleID, r.RoleName, u.IsActive
    FROM   dbo.APS_UserMaster u
  
  INNER  JOIN dbo.APS_RoleMaster r ON r.RoleID = u.RoleID
    WHERE  u.UserName = @UserName AND u.IsActive = 1;
END


Text
---------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------

/* Used by the controller to refuse a use case the caller is not mapped to,
   whatever id arrived on the request. */
CREATE   PROCEDURE dbo.APS_UserUseCaseMapping_Check
    @username  varchar(30),
    @usecaseid int,
    @isallowed tinyint OUTPUT
AS
BEG
IN
    SET NOCOUNT ON;

    SET @isallowed = CASE WHEN EXISTS (
        SELECT 1 FROM dbo.APS_UserUseCaseMapping
        WHERE UserName = @username AND UseCaseID = @usecaseid AND IsActive = 1)
        THEN 1 ELSE 0 END;
END


Text
---------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------

CREATE PROCEDURE dbo.API_Config_Application_GetList
    @CustomerId INT = NULL
AS
BEGIN
    SET NOCOUNT ON;
    SELECT ApplicationId, CustomerId, ApplicationCode, ApplicationName
    FROM dbo.API_Config_Application
    WHERE Enabled = 1
      AND (@Custo
merId IS NULL OR CustomerId = @CustomerId)
    ORDER BY ApplicationName;
END


Text
---------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------

CREATE PROCEDURE dbo.API_Config_Audit_GetRecent
    @Top INT = 100
AS
BEGIN
    SET NOCOUNT ON;
    SELECT TOP (@Top)
           AuditEventId, EventType, EntityType, EntityId, ActorName, DetailsJson, CreatedOn
    FROM dbo.API_Config_AuditEvent
    ORDER
 BY AuditEventId DESC;
END


Text
---------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------
/* ============================================================================
   Agentic PITSTOP - API Configurator
   Script 02 : Stored procedures (all prefixed API_Config_)
   Requires SQL Server 2016 or later (OPENJSON / FOR JSON / STRING_SPLIT).
  
 ============================================================================ */


/* ----------------------------------------------------------------------------
   Drop existing procedures so the script is re-runnable.
   -------------------------------
--------------------------------------------- */
--DECLARE @sql NVARCHAR(MAX) = N'';
--SELECT @sql = @sql + N'DROP PROCEDURE dbo.' + QUOTENAME(name) + N';' + CHAR(10)
--FROM sys.procedures
--WHERE name LIKE N'API[_]Config[_]%';
--IF LEN(@sql) > 0 EXEC sp_
executesql @sql;
--GO

/* ============================================================================
   AUDIT
   ============================================================================ */
CREATE PROCEDURE dbo.API_Config_Audit_Insert
    @EventType 
  NVARCHAR(80),
    @EntityType  NVARCHAR(60)     = NULL,
    @EntityId    INT = NULL,
    @ActorName   NVARCHAR(128)    = NULL,
    @DetailsJson NVARCHAR(MAX)    = NULL
AS
BEGIN
    SET NOCOUNT ON;
    INSERT INTO dbo.API_Config_AuditEvent (EventType, En
tityType, EntityId, ActorName, DetailsJson)
    VALUES (@EventType, @EntityType, @EntityId, @ActorName, @DetailsJson);
END


Text
---------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------

CREATE PROCEDURE dbo.API_Config_CheckRun_GetLatest
    @ConnectorVersionId INT,
    @RunType            NVARCHAR(30) = NULL
AS
BEGIN
    SET NOCOUNT ON;
    SELECT TOP (1) CheckRunId, RunType, ResultStatus, ResultJson, StartedOn, CompletedOn, RunBy
    F
ROM dbo.API_Config_CheckRun
    WHERE ConnectorVersionId = @ConnectorVersionId
      AND (@RunType IS NULL OR RunType = @RunType)
    ORDER BY StartedOn DESC;
END


Text
---------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------

/* ============================================================================
   CHECK RUNS - Validate Configuration / Connection Test evidence
   ============================================================================ */
CREATE PROCEDURE dbo.API_
Config_CheckRun_Insert
    @ConnectorVersionId INT,
    @RunType            NVARCHAR(30),
    @ResultStatus       NVARCHAR(20),
    @ResultJson         NVARCHAR(MAX) = NULL,
    @StartedOn       DATETIME  = NULL,
    @RunBy              NVARCHAR(128) = NU
LL
AS
BEGIN
    SET NOCOUNT ON;
    INSERT INTO dbo.API_Config_CheckRun
        (ConnectorVersionId, RunType, ResultStatus, ResultJson, StartedOn, CompletedOn, RunBy)
    VALUES
        (@ConnectorVersionId, @RunType, @ResultStatus, @ResultJson,
         
ISNULL(@StartedOn, GETDATE()), GETDATE(), @RunBy);
END


Text
---------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------

/* ============================================================================
   CONNECTOR CATALOG
   ============================================================================ */
CREATE PROCEDURE dbo.API_Config_Connector_GetCatalog
    @Search     N
VARCHAR(200)    = NULL,
    @CustomerId INT = NULL,
    @Status     NVARCHAR(20)     = NULL
AS
BEGIN
    SET NOCOUNT ON;

    SELECT
        v.ConnectorVersionId,
        c.ConnectorId,
        c.ConnectorName,
        c.ConnectorCode,
        c.Operation
Code,
        c.Enabled,
        cu.CustomerName,
        a.ApplicationName,
        v.VersionNumber,
        v.Status,
        v.Protocol,
        v.HttpMethod,
        v.BaseUrl,
        v.EndpointTemplate,
        v.ConfigHash,
        v.PublishedOn,
 
       v.ModifiedOn,
        AuthType = ISNULL(au.AuthType, N'None'),
        IsCurrentPublished = CASE WHEN c.CurrentPublishedVersionId = v.ConnectorVersionId THEN 1 ELSE 0 END
    FROM dbo.API_Config_ConnectorVersion v
    INNER JOIN dbo.API_Config_Conn
ector c    ON c.ConnectorId = v.ConnectorId
    INNER JOIN dbo.API_Config_Customer cu    ON cu.CustomerId = c.CustomerId
    INNER JOIN dbo.API_Config_Application a  ON a.ApplicationId = c.ApplicationId
    LEFT  JOIN dbo.API_Config_AuthConfig au  ON au.C
onnectorVersionId = v.ConnectorVersionId
    WHERE (@CustomerId IS NULL OR c.CustomerId = @CustomerId)
      AND (@Status IS NULL OR @Status = N'' OR v.Status = @Status)
      AND (@Search IS NULL OR @Search = N''
           OR c.ConnectorName LIKE N'%' +
 @Search + N'%'
           OR c.ConnectorCode LIKE N'%' + @Search + N'%'
           OR c.OperationCode LIKE N'%' + @Search + N'%'
           OR cu.CustomerName LIKE N'%' + @Search + N'%')
    ORDER BY c.ConnectorName, v.VersionNumber DESC;
END


Text
---------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------

/* Published connectors only - used by the simulator dropdown */
CREATE PROCEDURE dbo.API_Config_Connector_GetPublishedList
AS
BEGIN
    SET NOCOUNT ON;
    SELECT
        v.ConnectorVersionId,
        c.ConnectorId,
        c.ConnectorName,
        c.Co
nnectorCode,
        c.OperationCode,
        v.VersionNumber,
        v.HttpMethod,
        v.BaseUrl,
        v.EndpointTemplate,
        v.ExecutionModeAllowed,
        v.SampleSourceJson,
        cu.CustomerName
    FROM dbo.API_Config_Connector c
   
 INNER JOIN dbo.API_Config_ConnectorVersion v ON v.ConnectorVersionId = c.CurrentPublishedVersionId
    INNER JOIN dbo.API_Config_Customer cu        ON cu.CustomerId = c.CustomerId
    WHERE c.Enabled = 1
    ORDER BY c.ConnectorName;
END


Text
---------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------

CREATE PROCEDURE dbo.API_Config_Connector_SetActive
    @ConnectorId INT,
    @Enabled     BIT,
    @ActorName   NVARCHAR(128) = NULL
AS
BEGIN
    SET NOCOUNT ON;

    UPDATE dbo.API_Config_Connector
       SET Enabled    = @Enabled,
           ModifiedB
y = @ActorName,
           ModifiedOn = GETDATE()
     WHERE ConnectorId = @ConnectorId;

    EXEC dbo.API_Config_Audit_Insert
        @EventType  = N'CONNECTOR_ACTIVE_CHANGED',
        @EntityType = N'Connector',
        @EntityId   = @ConnectorId,
     
   @ActorName  = @ActorName,
        @DetailsJson= NULL;
END


Text
---------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------

/* ============================================================================
   CLONE A PUBLISHED VERSION INTO A NEW DRAFT
   ============================================================================ */
CREATE PROCEDURE dbo.API_Config_ConnectorVers
ion_Clone
    @SourceVersionId INT,
    @ActorName       NVARCHAR(128) = NULL,
    @NewVersionId    INT OUTPUT
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;

    DECLARE @connectorId INT, @versionNo INT;

    SELECT @connectorId = ConnectorId FROM d
bo.API_Config_ConnectorVersion WHERE ConnectorVersionId = @SourceVersionId;
    IF @connectorId IS NULL
    BEGIN
        RAISERROR (N'API_Config: source version not found.', 16, 1);
        RETURN;
    END

    BEGIN TRY
        BEGIN TRANSACTION;

     
   SELECT @versionNo = ISNULL(MAX(VersionNumber), 0) + 1
        FROM dbo.API_Config_ConnectorVersion WITH (UPDLOCK, HOLDLOCK)
        WHERE ConnectorId = @connectorId;

        INSERT INTO dbo.API_Config_ConnectorVersion
            (ConnectorId, Version
Number, Status, Protocol, ExecutionModeAllowed,
             HttpMethod, BaseUrlAlias, BaseUrl, EndpointTemplate, SampleSourceJson, PayloadTemplateJson,
             TimeoutSeconds, MaxAttempts, IdempotencyHeader, CorrelationHeader, ResponseType,
        
     ResultMappingJson, IsRetrySafe, CreatedBy, ModifiedOn, ModifiedBy)
        SELECT ConnectorId, @versionNo, N'Draft', Protocol, ExecutionModeAllowed,
               HttpMethod, BaseUrlAlias, BaseUrl, EndpointTemplate, SampleSourceJson, PayloadTemplate
Json,
               TimeoutSeconds, MaxAttempts, IdempotencyHeader, CorrelationHeader, ResponseType,
               ResultMappingJson, IsRetrySafe, @ActorName, GETDATE(), @ActorName
        FROM dbo.API_Config_ConnectorVersion
        WHERE ConnectorVers
ionId = @SourceVersionId;

        SET @NewVersionId = CONVERT(INT, SCOPE_IDENTITY());

        INSERT INTO dbo.API_Config_Header (ConnectorVersionId, HeaderName, HeaderValueTemplate, SortOrder)
        SELECT @NewVersionId, HeaderName, HeaderValueTemplat
e, SortOrder
        FROM dbo.API_Config_Header WHERE ConnectorVersionId = @SourceVersionId;

        INSERT INTO dbo.API_Config_QueryParameter (ConnectorVersionId, ParameterName, ParameterValueTemplate, SortOrder)
        SELECT @NewVersionId, ParameterN
ame, ParameterValueTemplate, SortOrder
        FROM dbo.API_Config_QueryParameter WHERE ConnectorVersionId = @SourceVersionId;

        INSERT INTO dbo.API_Config_AllowedHost (ConnectorVersionId, HostName, PortNumber)
        SELECT @NewVersionId, HostNam
e, PortNumber
        FROM dbo.API_Config_AllowedHost WHERE ConnectorVersionId = @SourceVersionId;

        INSERT INTO dbo.API_Config_SuccessCode (ConnectorVersionId, StatusCode)
        SELECT @NewVersionId, StatusCode
        FROM dbo.API_Config_Succes
sCode WHERE ConnectorVersionId = @SourceVersionId;

        INSERT INTO dbo.API_Config_AuthConfig
            (ConnectorVersionId, AuthType, SecretStoreAlias, ClientIdSecretRef, ClientSecretSecretRef,
             TokenEndpoint, Scope, Audience, ApiKeyNam
e, ApiKeyLocation, ApiKeySecretRef,
             BearerTokenSecretRef, UsernameSecretRef, PasswordSecretRef, CertificateSecretRef,
             CustomPluginKey, AdditionalSettingsJson)
        SELECT @NewVersionId, AuthType, SecretStoreAlias, ClientIdSecr
etRef, ClientSecretSecretRef,
               TokenEndpoint, Scope, Audience, ApiKeyName, ApiKeyLocation, ApiKeySecretRef,
               BearerTokenSecretRef, UsernameSecretRef, PasswordSecretRef, CertificateSecretRef,
               CustomPluginKey, Addi
tionalSettingsJson
        FROM dbo.API_Config_AuthConfig WHERE ConnectorVersionId = @SourceVersionId;

        EXEC dbo.API_Config_Audit_Insert
            @EventType   = N'CONNECTOR_VERSION_CLONED',
            @EntityType  = N'ConnectorVersion',
      
      @EntityId    = @NewVersionId,
            @ActorName   = @ActorName,
            @DetailsJson = NULL;

        COMMIT TRANSACTION;
    END TRY
    BEGIN CATCH
        IF XACT_STATE() <> 0 ROLLBACK TRANSACTION;
        THROW;
    END CATCH

    SELEC
T NewVersionId = @NewVersionId, VersionNumber = @versionNo;
END


Text
---------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------

/* ============================================================================
   LOAD ONE VERSION - 6 result sets consumed by the wizard
   ============================================================================ */
CREATE PROCEDURE dbo.API_Config_
ConnectorVersion_Get
    @ConnectorVersionId INT
AS
BEGIN
    SET NOCOUNT ON;

    /* 0 - version + connector header */
    SELECT
        v.ConnectorVersionId, v.ConnectorId, v.VersionNumber, v.Status,
        v.Protocol, v.ExecutionModeAllowed, v.HttpMe
thod, v.BaseUrlAlias, v.BaseUrl,
        v.EndpointTemplate, v.SampleSourceJson, v.PayloadTemplateJson,
        v.TimeoutSeconds, v.MaxAttempts, v.IdempotencyHeader, v.CorrelationHeader,
        v.ResponseType, v.ResultMappingJson, v.IsRetrySafe,
        
v.PublishedSnapshotJson, v.ConfigHash, v.PublishedOn, v.PublishedBy,
        --RowVersionHex = CONVERT(NVARCHAR(50), v.RowVersion, 1),
        RowVersionHex = CONVERT(NVARCHAR(50), CONVERT(VARBINARY(8), v.RowVersion), 1),
        c.CustomerId, c.Applicati
onId, c.ConnectorCode, c.ConnectorName, c.OperationCode, c.Enabled,
        cu.CustomerName, a.ApplicationName
    FROM dbo.API_Config_ConnectorVersion v
    INNER JOIN dbo.API_Config_Connector c   ON c.ConnectorId = v.ConnectorId
    INNER JOIN dbo.API_C
onfig_Customer cu   ON cu.CustomerId = c.CustomerId
    INNER JOIN dbo.API_Config_Application a ON a.ApplicationId = c.ApplicationId
    WHERE v.ConnectorVersionId = @ConnectorVersionId;

    /* 1 - headers */
    SELECT HeaderName, HeaderValueTemplate, S
ortOrder
    FROM dbo.API_Config_Header
    WHERE ConnectorVersionId = @ConnectorVersionId AND Enabled = 1
    ORDER BY SortOrder, HeaderName;

    /* 2 - query parameters */
    SELECT ParameterName, ParameterValueTemplate, SortOrder
    FROM dbo.API_Con
fig_QueryParameter
    WHERE ConnectorVersionId = @ConnectorVersionId AND Enabled = 1
    ORDER BY SortOrder, ParameterName;

    /* 3 - allowed hosts */
    SELECT HostName, PortNumber
    FROM dbo.API_Config_AllowedHost
    WHERE ConnectorVersionId = @C
onnectorVersionId AND Enabled = 1
    ORDER BY HostName;

    /* 4 - success codes */
    SELECT StatusCode
    FROM dbo.API_Config_SuccessCode
    WHERE ConnectorVersionId = @ConnectorVersionId AND Enabled = 1
    ORDER BY StatusCode;

    /* 5 - auth co
nfiguration (references only) */
    SELECT AuthType, SecretStoreAlias, ClientIdSecretRef, ClientSecretSecretRef,
           TokenEndpoint, Scope, Audience, ApiKeyName, ApiKeyLocation, ApiKeySecretRef,
           BearerTokenSecretRef, UsernameSecretRef, P
asswordSecretRef,
           CertificateSecretRef, CustomPluginKey, AdditionalSettingsJson
    FROM dbo.API_Config_AuthConfig
    WHERE ConnectorVersionId = @ConnectorVersionId AND Enabled = 1;

    /* 6 - last check runs */
    SELECT TOP (5) RunType, Re
sultStatus, ResultJson, StartedOn, CompletedOn, RunBy
    FROM dbo.API_Config_CheckRun
    WHERE ConnectorVersionId = @ConnectorVersionId
    ORDER BY StartedOn DESC;
END


Text
---------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------

/* ============================================================================
   PUBLISH - freeze the snapshot, compute SHA-256 ConfigHash, mark immutable
   ============================================================================ */
CREATE PROCEDU
RE dbo.API_Config_ConnectorVersion_Publish
    @ConnectorVersionId    INT,
    @ActorName             NVARCHAR(128) = NULL,
    @RequireConnectionTest BIT           = 0
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;

    DECLARE @status NVARCHAR(20),
 @connectorId INT;

    SELECT @status = v.Status, @connectorId = v.ConnectorId
    FROM dbo.API_Config_ConnectorVersion v
    WHERE v.ConnectorVersionId = @ConnectorVersionId;

    IF @status IS NULL
    BEGIN
        RAISERROR (N'API_Config: connector v
ersion not found.', 16, 1);
        RETURN;
    END
    IF @status <> N'Draft'
    BEGIN
        RAISERROR (N'API_Config: only a Draft version can be published.', 16, 1);
        RETURN;
    END

    /* ---- server-side revalidation of the publish rules -
----------------- */
    DECLARE @errors NVARCHAR(MAX) = N'';

    SELECT @errors = @errors +
        CASE WHEN NULLIF(LTRIM(RTRIM(v.HttpMethod)), N'') IS NULL      THEN N'HTTP method is required. ' ELSE N'' END +
        CASE WHEN NULLIF(LTRIM(RTRIM(v.Ba
seUrl)), N'') IS NULL         THEN N'Base URL is required. ' ELSE N'' END +
        CASE WHEN v.BaseUrl IS NOT NULL AND LOWER(LEFT(v.BaseUrl, 8)) <> N'https://'
                                                                        THEN N'Base URL must u
se HTTPS. ' ELSE N'' END +
        CASE WHEN NULLIF(LTRIM(RTRIM(v.EndpointTemplate)), N'') IS NULL THEN N'Relative endpoint template is required. ' ELSE N'' END +
        CASE WHEN v.EndpointTemplate IS NOT NULL AND LEFT(v.EndpointTemplate, 1) <> N'/'
   
                                                                     THEN N'Endpoint template must start with a forward slash. ' ELSE N'' END +
        CASE WHEN v.Protocol <> N'REST'                                 THEN N'Only the REST executor is availa
ble in this release. ' ELSE N'' END +
        CASE WHEN v.PayloadTemplateJson IS NOT NULL AND ISJSON(v.PayloadTemplateJson) = 0
                                                                        THEN N'Payload template is not valid JSON. ' ELSE N'' E
ND +
        CASE WHEN NOT EXISTS (SELECT 1 FROM dbo.API_Config_SuccessCode s WHERE s.ConnectorVersionId = v.ConnectorVersionId AND s.Enabled = 1)
                                                                        THEN N'At least one success status c
ode is required. ' ELSE N'' END +
        CASE WHEN NOT EXISTS (SELECT 1 FROM dbo.API_Config_AllowedHost h WHERE h.ConnectorVersionId = v.ConnectorVersionId AND h.Enabled = 1)
                                                                        THEN N'
At least one approved host is required. ' ELSE N'' END
    FROM dbo.API_Config_ConnectorVersion v
    WHERE v.ConnectorVersionId = @ConnectorVersionId;

    /* the base URL host must appear in the allowlist */
    DECLARE @baseUrl NVARCHAR(500), @baseHost
 NVARCHAR(255);
    SELECT @baseUrl = BaseUrl FROM dbo.API_Config_ConnectorVersion WHERE ConnectorVersionId = @ConnectorVersionId;

    IF @baseUrl IS NOT NULL AND CHARINDEX(N'://', @baseUrl) > 0
    BEGIN
        SET @baseHost = SUBSTRING(@baseUrl, CHARI
NDEX(N'://', @baseUrl) + 3, 500);
        IF CHARINDEX(N'/', @baseHost) > 0 SET @baseHost = LEFT(@baseHost, CHARINDEX(N'/', @baseHost) - 1);
        IF CHARINDEX(N':', @baseHost) > 0 SET @baseHost = LEFT(@baseHost, CHARINDEX(N':', @baseHost) - 1);
       
 SET @baseHost = LOWER(@baseHost);

        IF NOT EXISTS (SELECT 1 FROM dbo.API_Config_AllowedHost
                       WHERE ConnectorVersionId = @ConnectorVersionId AND HostName = @baseHost AND Enabled = 1)
            SET @errors = @errors + N'The b
ase URL host is not in the approved host allowlist. ';
    END

    IF @RequireConnectionTest = 1
       AND NOT EXISTS (SELECT 1 FROM dbo.API_Config_CheckRun
        WHERE ConnectorVersionId = @ConnectorVersionId
                         AND RunType = N'
ConnectionTest'
                         AND ResultStatus = N'Passed'
                         AND StartedOn >= DATEADD(DAY, -7, GETDATE()))
        SET @errors = @errors + N'A passing connection test within the last 7 days is required. ';

    IF LEN(@er
rors) > 0
    BEGIN
        /* RAISERROR substitution arguments cannot be an NVARCHAR(MAX) value. */
        DECLARE @errorText NVARCHAR(2000) = LEFT(@errors, 2000);
        RAISERROR (N'API_Config publish blocked: %s', 16, 1, @errorText);
        RETURN;

    END

    BEGIN TRY
        BEGIN TRANSACTION;

        /* ---- canonical snapshot (deterministic ordering) ---------------- */
        DECLARE @snapshot NVARCHAR(MAX);

        SET @snapshot =
        (
            SELECT
                v.ConnectorV
ersionId,
                c.ConnectorCode,
                c.ConnectorName,
                c.OperationCode,
                v.VersionNumber,
                v.Protocol,
                v.ExecutionModeAllowed,
                v.HttpMethod,
               
 v.BaseUrlAlias,
                v.BaseUrl,
                v.EndpointTemplate,
                v.TimeoutSeconds,
                v.MaxAttempts,
                v.IdempotencyHeader,
                v.CorrelationHeader,
                v.ResponseType,
    
            v.IsRetrySafe,
                PayloadTemplate = v.PayloadTemplateJson,
                ResultMapping   = v.ResultMappingJson,
                Headers = (SELECT h.HeaderName AS [name], h.HeaderValueTemplate AS [value]
                         
  FROM dbo.API_Config_Header h
                           WHERE h.ConnectorVersionId = v.ConnectorVersionId AND h.Enabled = 1
                           ORDER BY h.HeaderName, h.HeaderValueTemplate
                           FOR JSON PATH),
              
  QueryParameters = (SELECT q.ParameterName AS [name], q.ParameterValueTemplate AS [value]
                           FROM dbo.API_Config_QueryParameter q
                           WHERE q.ConnectorVersionId = v.ConnectorVersionId AND q.Enabled = 1
     
                      ORDER BY q.ParameterName, q.ParameterValueTemplate
                           FOR JSON PATH),
                AllowedHosts = (SELECT a.HostName AS [host], a.PortNumber AS [port]
                           FROM dbo.API_Config_AllowedH
ost a
                           WHERE a.ConnectorVersionId = v.ConnectorVersionId AND a.Enabled = 1
                           ORDER BY a.HostName
                           FOR JSON PATH),
                SuccessCodes = (SELECT s.StatusCode AS [code]
  
                         FROM dbo.API_Config_SuccessCode s
                           WHERE s.ConnectorVersionId = v.ConnectorVersionId AND s.Enabled = 1
                           ORDER BY s.StatusCode
                           FOR JSON PATH),
         
       Auth = (SELECT au.AuthType, au.SecretStoreAlias, au.ClientIdSecretRef, au.ClientSecretSecretRef,
                               au.TokenEndpoint, au.Scope, au.Audience, au.ApiKeyName, au.ApiKeyLocation,
                               au.ApiKeySecre
tRef, au.BearerTokenSecretRef, au.UsernameSecretRef,
                               au.PasswordSecretRef, au.CertificateSecretRef, au.CustomPluginKey
                        FROM dbo.API_Config_AuthConfig au
                        WHERE au.ConnectorVersi
onId = v.ConnectorVersionId AND au.Enabled = 1
                        FOR JSON PATH, WITHOUT_ARRAY_WRAPPER)
            FROM dbo.API_Config_ConnectorVersion v
            INNER JOIN dbo.API_Config_Connector c ON c.ConnectorId = v.ConnectorId
            
WHERE v.ConnectorVersionId = @ConnectorVersionId
            FOR JSON PATH, WITHOUT_ARRAY_WRAPPER
        );

        DECLARE @configHash CHAR(64) =
            CONVERT(CHAR(64), HASHBYTES('SHA2_256', @snapshot), 2);

        UPDATE dbo.API_Config_Connect
orVersion
           SET Status                = N'Published',
               PublishedSnapshotJson = @snapshot,
               ConfigHash            = @configHash,
               PublishedOn        = GETDATE(),
               PublishedBy           = @Act
orName,
               ModifiedOn         = GETDATE(),
               ModifiedBy            = @ActorName
         WHERE ConnectorVersionId = @ConnectorVersionId;

        UPDATE dbo.API_Config_Connector
           SET CurrentPublishedVersionId = @Connecto
rVersionId
         WHERE ConnectorId = @connectorId;

        EXEC dbo.API_Config_Audit_Insert
            @EventType   = N'CONNECTOR_VERSION_PUBLISHED',
            @EntityType  = N'ConnectorVersion',
            @EntityId    = @ConnectorVersionId,
    
        @ActorName   = @ActorName,
            @DetailsJson = @configHash;

        COMMIT TRANSACTION;
    END TRY
    BEGIN CATCH
        IF XACT_STATE() <> 0 ROLLBACK TRANSACTION;
        THROW;
    END CATCH

    SELECT ConnectorVersionId = @Connector
VersionId,
           ConfigHash         = @configHash,
           Status             = N'Published';
END


Text
---------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------

/* ============================================================================
   SAVE DRAFT - creates or updates a Draft version in one transaction
   Child collections arrive as JSON arrays, success codes as a CSV string.
   ==========================
================================================== */
CREATE PROCEDURE dbo.API_Config_ConnectorVersion_SaveDraft
    @ConnectorVersionId   INT = NULL,
    @CustomerId           INT,
    @ApplicationId        INT,
    @ConnectorCode        NVARCHAR(100),
 
   @ConnectorName        NVARCHAR(200),
    @OperationCode        NVARCHAR(100),
    @Protocol             NVARCHAR(20)   = N'REST',
    @ExecutionModeAllowed NVARCHAR(30)   = N'Both',
    @HttpMethod           NVARCHAR(10)   = NULL,
    @BaseUrlAlias    
     NVARCHAR(100)  = NULL,
    @BaseUrl              NVARCHAR(500)  = NULL,
    @EndpointTemplate     NVARCHAR(500)  = NULL,
    @SampleSourceJson     NVARCHAR(MAX)  = NULL,
    @PayloadTemplateJson  NVARCHAR(MAX)  = NULL,
    @TimeoutSeconds       INT  
          = 30,
    @MaxAttempts          INT            = 3,
    @IdempotencyHeader    NVARCHAR(100)  = NULL,
    @CorrelationHeader    NVARCHAR(100)  = NULL,
    @ResponseType         NVARCHAR(20)   = N'JSON',
    @ResultMappingJson    NVARCHAR(MAX)  = 
NULL,
    @IsRetrySafe          BIT            = 0,
    @AuthJson             NVARCHAR(MAX)  = NULL,   -- single object
    @HeadersJson          NVARCHAR(MAX)  = NULL,   -- [{"name":"","value":""}]
    @QueryParamsJson      NVARCHAR(MAX)  = NULL,   -- [{
"name":"","value":""}]
    @AllowedHostsJson     NVARCHAR(MAX)  = NULL,   -- [{"host":"","port":null}]
    @SuccessCodesCsv      NVARCHAR(200)  = NULL,   -- "200,201,202"
    @RowVersionHex        NVARCHAR(50)   = NULL,   -- optimistic concurrency token, 
e.g. 0x00000000000007D1
    @ActorName            NVARCHAR(128)  = NULL,
    @NewConnectorVersionId INT OUTPUT,
    @NewConnectorId        INT OUTPUT,
    @VersionNumberOut      INT              OUTPUT
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;


    DECLARE @connectorId INT;
    DECLARE @versionId   INT = @ConnectorVersionId;
    DECLARE @versionNo   INT;

    /* -- guard rails ---------------------------------------------------- */
    IF NOT EXISTS (SELECT 1 FROM dbo.API_Config_Application
    
               WHERE ApplicationId = @ApplicationId AND CustomerId = @CustomerId AND Enabled = 1)
    BEGIN
        RAISERROR (N'API_Config: the selected application does not belong to the selected customer.', 16, 1);
        RETURN;
    END

    IF @vers
ionId IS NOT NULL
       AND EXISTS (SELECT 1 FROM dbo.API_Config_ConnectorVersion
                   WHERE ConnectorVersionId = @versionId AND Status = N'Published')
    BEGIN
        RAISERROR (N'API_Config: a Published version cannot be edited. Clone i
t as a new Draft version.', 16, 1);
        RETURN;
    END

    BEGIN TRY
        BEGIN TRANSACTION;

        IF @versionId IS NULL
        BEGIN
            /* Re-use the logical connector when the code already exists for the customer. */
            SE
LECT @connectorId = ConnectorId
            FROM dbo.API_Config_Connector
            WHERE CustomerId = @CustomerId AND ConnectorCode = @ConnectorCode;

            IF @connectorId IS NULL
            BEGIN
                INSERT INTO dbo.API_Config_Conn
ector
                    (CustomerId, ApplicationId, ConnectorCode, ConnectorName, OperationCode, CreatedBy)
                VALUES
                    (@CustomerId, @ApplicationId, @ConnectorCode, @ConnectorName, @OperationCode, @ActorName);

          
      SET @connectorId = CONVERT(INT, SCOPE_IDENTITY());
            END
            ELSE
            BEGIN
                UPDATE dbo.API_Config_Connector
                   SET ApplicationId = @ApplicationId,
                       ConnectorName = @Conn
ectorName,
                       OperationCode = @OperationCode,
                       ModifiedBy    = @ActorName,
                       ModifiedOn    = GETDATE()
         WHERE ConnectorId = @connectorId;
            END

            SELECT @versionNo
 = ISNULL(MAX(VersionNumber), 0) + 1
            FROM dbo.API_Config_ConnectorVersion WITH (UPDLOCK, HOLDLOCK)
            WHERE ConnectorId = @connectorId;

            INSERT INTO dbo.API_Config_ConnectorVersion
                (ConnectorId, VersionNumb
er, Status, Protocol, ExecutionModeAllowed,
                 HttpMethod, BaseUrlAlias, BaseUrl, EndpointTemplate, SampleSourceJson, PayloadTemplateJson,
                 TimeoutSeconds, MaxAttempts, IdempotencyHeader, CorrelationHeader, ResponseType,
    
             ResultMappingJson, IsRetrySafe, CreatedBy, ModifiedOn, ModifiedBy)
            VALUES
                (@connectorId, @versionNo, N'Draft', @Protocol, @ExecutionModeAllowed,
                 @HttpMethod, @BaseUrlAlias, @BaseUrl, @EndpointTempl
ate, @SampleSourceJson, @PayloadTemplateJson,
                 @TimeoutSeconds, @MaxAttempts, @IdempotencyHeader, @CorrelationHeader, @ResponseType,
                 @ResultMappingJson, @IsRetrySafe, @ActorName, GETDATE(), @ActorName);

            SET @v
ersionId = CONVERT(INT, SCOPE_IDENTITY());
        END
        ELSE
        BEGIN
            SELECT @connectorId = ConnectorId, @versionNo = VersionNumber
            FROM dbo.API_Config_ConnectorVersion WITH (UPDLOCK)
            WHERE ConnectorVersionI
d = @versionId;

            IF @connectorId IS NULL
            BEGIN
                RAISERROR (N'API_Config: connector version not found.', 16, 1);
                ROLLBACK TRANSACTION;
                RETURN;
            END

            /* Optimistic
 concurrency: another user must not be silently overwritten. */
            IF NULLIF(@RowVersionHex, N'') IS NOT NULL
               AND NOT EXISTS (SELECT 1 FROM dbo.API_Config_ConnectorVersion
                               WHERE ConnectorVersionId = @
versionId
                                 AND RowVersion = CONVERT(VARBINARY(8), @RowVersionHex, 1))
            BEGIN
                RAISERROR (N'API_Config: this draft was changed by another user after you loaded it. Reload the draft and re-apply your
 changes.', 16, 1);
                ROLLBACK TRANSACTION;
                RETURN;
            END

            UPDATE dbo.API_Config_Connector
               SET CustomerId    = @CustomerId,
                   ApplicationId = @ApplicationId,
             
      ConnectorCode = @ConnectorCode,
                   ConnectorName = @ConnectorName,
                   OperationCode = @OperationCode,
                   ModifiedBy    = @ActorName,
                   ModifiedOn    = GETDATE()
             WHERE Conn
ectorId = @connectorId;

            UPDATE dbo.API_Config_ConnectorVersion
               SET Protocol             = @Protocol,
                   ExecutionModeAllowed = @ExecutionModeAllowed,
                   HttpMethod           = @HttpMethod,
      
             BaseUrlAlias         = @BaseUrlAlias,
                   BaseUrl              = @BaseUrl,
                   EndpointTemplate     = @EndpointTemplate,
                   SampleSourceJson     = @SampleSourceJson,
                   PayloadTemp
lateJson  = @PayloadTemplateJson,
                   TimeoutSeconds       = @TimeoutSeconds,
                   MaxAttempts          = @MaxAttempts,
                   IdempotencyHeader    = @IdempotencyHeader,
                   CorrelationHeader    = @C
orrelationHeader,
                   ResponseType         = @ResponseType,
                   ResultMappingJson    = @ResultMappingJson,
                   IsRetrySafe          = @IsRetrySafe,
                   ModifiedOn        = GETDATE(),
            
       ModifiedBy           = @ActorName
             WHERE ConnectorVersionId = @versionId;
        END

        /* -- child rows: delete and re-insert (low volume configuration) --- */
        DELETE FROM dbo.API_Config_Header         WHERE ConnectorVer
sionId = @versionId;
        DELETE FROM dbo.API_Config_QueryParameter WHERE ConnectorVersionId = @versionId;
        DELETE FROM dbo.API_Config_AllowedHost    WHERE ConnectorVersionId = @versionId;
        DELETE FROM dbo.API_Config_SuccessCode    WHERE 
ConnectorVersionId = @versionId;
        DELETE FROM dbo.API_Config_AuthConfig     WHERE ConnectorVersionId = @versionId;

        IF @HeadersJson IS NOT NULL AND ISJSON(@HeadersJson) = 1
            INSERT INTO dbo.API_Config_Header (ConnectorVersionId, 
HeaderName, HeaderValueTemplate, SortOrder)
            SELECT @versionId, j.[name], j.[value], j.[key]
            FROM OPENJSON(@HeadersJson)
                 WITH ([name] NVARCHAR(100) N'$.name',
                       [value] NVARCHAR(500) N'$.value',

                       [key] INT N'$.sort') j
            WHERE NULLIF(LTRIM(RTRIM(j.[name])), N'') IS NOT NULL
              /* hop-by-hop and restricted headers are rejected */
              AND LOWER(j.[name]) NOT IN (N'host', N'content-length', N'con
nection',
                                          N'transfer-encoding', N'upgrade', N'keep-alive',
                                          N'proxy-authorization', N'te', N'trailer');

        IF @QueryParamsJson IS NOT NULL AND ISJSON(@QueryParamsJson
) = 1
            INSERT INTO dbo.API_Config_QueryParameter (ConnectorVersionId, ParameterName, ParameterValueTemplate, SortOrder)
            SELECT @versionId, j.[name], j.[value], j.[key]
            FROM OPENJSON(@QueryParamsJson)
                 WIT
H ([name] NVARCHAR(100) N'$.name',
                       [value] NVARCHAR(500) N'$.value',
                       [key] INT N'$.sort') j
            WHERE NULLIF(LTRIM(RTRIM(j.[name])), N'') IS NOT NULL;

        IF @AllowedHostsJson IS NOT NULL AND ISJS
ON(@AllowedHostsJson) = 1
            INSERT INTO dbo.API_Config_AllowedHost (ConnectorVersionId, HostName, PortNumber)
            SELECT DISTINCT @versionId, LOWER(LTRIM(RTRIM(j.[host]))), j.[port]
            FROM OPENJSON(@AllowedHostsJson)
          
       WITH ([host] NVARCHAR(255) N'$.host',
                       [port] INT N'$.port') j
            WHERE NULLIF(LTRIM(RTRIM(j.[host])), N'') IS NOT NULL;

        IF NULLIF(LTRIM(RTRIM(@SuccessCodesCsv)), N'') IS NOT NULL
            INSERT INTO dbo.
API_Config_SuccessCode (ConnectorVersionId, StatusCode)
            SELECT DISTINCT @versionId, TRY_CONVERT(INT, LTRIM(RTRIM(s.[value])))
            FROM STRING_SPLIT(@SuccessCodesCsv, N',') s
            WHERE TRY_CONVERT(INT, LTRIM(RTRIM(s.[value]))) B
ETWEEN 100 AND 599;

        IF @AuthJson IS NOT NULL AND ISJSON(@AuthJson) = 1
            INSERT INTO dbo.API_Config_AuthConfig
                (ConnectorVersionId, AuthType, SecretStoreAlias, ClientIdSecretRef, ClientSecretSecretRef,
                 T
okenEndpoint, Scope, Audience, ApiKeyName, ApiKeyLocation, ApiKeySecretRef,
                 BearerTokenSecretRef, UsernameSecretRef, PasswordSecretRef, CertificateSecretRef,
                 CustomPluginKey, AdditionalSettingsJson)
            SELECT @ve
rsionId, ISNULL(j.AuthType, N'None'), j.SecretStoreAlias, j.ClientIdSecretRef,
                   j.ClientSecretSecretRef, j.TokenEndpoint, j.Scope, j.Audience, j.ApiKeyName,
                   j.ApiKeyLocation, j.ApiKeySecretRef, j.BearerTokenSecretRef, 
j.UsernameSecretRef,
                   j.PasswordSecretRef, j.CertificateSecretRef, j.CustomPluginKey, j.AdditionalSettingsJson
            FROM OPENJSON(@AuthJson)
                 WITH (AuthType NVARCHAR(50), SecretStoreAlias NVARCHAR(100),
           
            ClientIdSecretRef NVARCHAR(300), ClientSecretSecretRef NVARCHAR(300),
                       TokenEndpoint NVARCHAR(500), Scope NVARCHAR(300), Audience NVARCHAR(300),
                       ApiKeyName NVARCHAR(100), ApiKeyLocation NVARCHAR(20)
, ApiKeySecretRef NVARCHAR(300),
                       BearerTokenSecretRef NVARCHAR(300), UsernameSecretRef NVARCHAR(300),
                       PasswordSecretRef NVARCHAR(300), CertificateSecretRef NVARCHAR(300),
                       CustomPluginKey
 NVARCHAR(100), AdditionalSettingsJson NVARCHAR(MAX) N'$.AdditionalSettingsJson' AS JSON) j;

        EXEC dbo.API_Config_Audit_Insert
            @EventType   = N'CONNECTOR_DRAFT_SAVED',
            @EntityType  = N'ConnectorVersion',
            @Entity
Id    = @versionId,
            @ActorName   = @ActorName,
            @DetailsJson = NULL;

        COMMIT TRANSACTION;
    END TRY
    BEGIN CATCH
        IF XACT_STATE() <> 0 ROLLBACK TRANSACTION;
        THROW;
    END CATCH

    SET @NewConnectorVers
ionId = @versionId;
    SET @NewConnectorId        = @connectorId;
    SET @VersionNumberOut      = @versionNo;

    SELECT ConnectorVersionId = @versionId,
           ConnectorId        = @connectorId,
           VersionNumber      = @versionNo;
END


Text
---------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------

/* ============================================================================
   MASTER DATA LOOKUPS
   ============================================================================ */
CREATE PROCEDURE dbo.API_Config_Customer_GetList
AS
BEGIN
    SET NO
COUNT ON;
    SELECT CustomerId, CustomerCode, CustomerName
    FROM dbo.API_Config_Customer
    WHERE Enabled = 1
    ORDER BY CustomerName;
END


Text
---------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------

/* ============================================================================
   DASHBOARD
   ============================================================================ */
CREATE PROCEDURE dbo.API_Config_Dashboard_GetMetrics
    @ReportingDays INT = 
30
AS
BEGIN
    SET NOCOUNT ON;

    DECLARE @from DATETIME = DATEADD(DAY, -@ReportingDays, GETDATE());

    SELECT
        PublishedConnectors = (SELECT COUNT(*) FROM dbo.API_Config_Connector
                               WHERE Enabled = 1 AND CurrentPu
blishedVersionId IS NOT NULL),
        DraftConnectors     = (SELECT COUNT(*) FROM dbo.API_Config_ConnectorVersion
                               WHERE Status = N'Draft'),
        PendingApprovals    = (SELECT COUNT(*) FROM dbo.API_Config_ExecutionApprova
l
                               WHERE Status = N'Pending'),
        SuccessfulExecutions= (SELECT COUNT(*) FROM dbo.API_Config_ExecutionRequest
                               WHERE Status = N'Succeeded' AND CreatedOn >= @from),
        FailedExecutions  
  = (SELECT COUNT(*) FROM dbo.API_Config_ExecutionRequest
                               WHERE Status IN (N'BusinessFailed', N'TechnicalFailed', N'UnknownOutcome')
                                 AND CreatedOn >= @from),
        OpenExceptions      = (SE
LECT COUNT(*) FROM dbo.API_Config_ExecutionException
                               WHERE Status = N'Open');

    /* Second result set - recent activity strip */
    SELECT TOP (8)
           e.ExecutionId,
           e.ExecutionUid,
           c.Connecto
rName,
           v.VersionNumber,
           e.Status,
           e.ExecutionMode,
           e.CreatedOn
    FROM dbo.API_Config_ExecutionRequest e
    INNER JOIN dbo.API_Config_Connector c        ON c.ConnectorId = e.ConnectorId
    INNER JOIN dbo.API_
Config_ConnectorVersion v ON v.ConnectorVersionId = e.ConnectorVersionId
    ORDER BY e.CreatedOn DESC;
END


Text
---------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------

/* ----------------------------------------------------------------------------
   APPROVE - freezes the approval hash over the exact reviewed package
   ---------------------------------------------------------------------------- */
CREATE PROCEDURE dbo
.API_Config_Execution_Approve
    @ExecutionId INT,
    @ActorName   NVARCHAR(128) = NULL,
    @Comment     NVARCHAR(1000) = NULL
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;

    DECLARE @approvalStatus NVARCHAR(20), @mode NVARCHAR(30), @approvalH
ash CHAR(64);

    SELECT @approvalStatus = ap.Status, @mode = e.ExecutionMode
    FROM dbo.API_Config_ExecutionApproval ap
    INNER JOIN dbo.API_Config_ExecutionRequest e ON e.ExecutionId = ap.ExecutionId
    WHERE ap.ExecutionId = @ExecutionId;

    IF
 @approvalStatus IS NULL
    BEGIN
        RAISERROR (N'API_Config: execution not found.', 16, 1);
        RETURN;
    END
    IF @approvalStatus <> N'Pending'
    BEGIN
        RAISERROR (N'API_Config: only a Pending approval can be approved.', 16, 1);
 
       RETURN;
    END

    BEGIN TRY
        BEGIN TRANSACTION;

        SELECT @approvalHash = CONVERT(CHAR(64), HASHBYTES('SHA2_256',
                   CONVERT(NVARCHAR(MAX), e.ExecutionId)    + N'|' +
                   CONVERT(NVARCHAR(MAX), e.Resol
vedMethod) + N'|' +
                   CONVERT(NVARCHAR(MAX), e.ResolvedUrl)    + N'|' +
                   ISNULL(e.ResolvedPayloadJson, N'')       + N'|' +
                   ISNULL(e.ConfigHash, N'')), 2)
        FROM dbo.API_Config_ExecutionRequest e 
WITH (UPDLOCK)
        WHERE e.ExecutionId = @ExecutionId;

        UPDATE dbo.API_Config_ExecutionApproval
           SET Status          = N'Approved',
               ApprovalHash    = @approvalHash,
               HashScheme      = N'PITSTOP-CANONICAL-
V1',
               DecidedOn    = GETDATE(),
               DecidedBy       = @ActorName,
               DecisionComment = @Comment
         WHERE ExecutionId = @ExecutionId AND Status = N'Pending';

        IF @@ROWCOUNT = 0
        BEGIN
            RA
ISERROR (N'API_Config: the approval was changed by another user. Reload and try again.', 16, 1);
            ROLLBACK TRANSACTION;
            RETURN;
        END

        /* Unattended-after-approval goes straight to the queue with an outbox row. */
    
    IF @mode = N'UnattendedAfterApproval'
        BEGIN
            UPDATE dbo.API_Config_ExecutionRequest
               SET Status = N'Queued'
             WHERE ExecutionId = @ExecutionId;

            INSERT INTO dbo.API_Config_OutboxMessage (Executio
nId, MessageType, PayloadJson)
            VALUES (@ExecutionId, N'ExecuteApprovedPackage',
                    N'{"executionId":' + CONVERT(NVARCHAR(20), @ExecutionId) + N'}');
        END
        ELSE
        BEGIN
            UPDATE dbo.API_Config_Exec
utionRequest
               SET Status = N'ApprovedAwaitingExecution'
             WHERE ExecutionId = @ExecutionId;
        END

        EXEC dbo.API_Config_Audit_Insert
            @EventType  = N'EXECUTION_APPROVED',
            @EntityType = N'Executi
onRequest',
            @EntityId   = @ExecutionId,
            @ActorName  = @ActorName;

        COMMIT TRANSACTION;
    END TRY
    BEGIN CATCH
        IF XACT_STATE() <> 0 ROLLBACK TRANSACTION;
        THROW;
    END CATCH

    SELECT ExecutionId = @E
xecutionId, ApprovalHash = @approvalHash;
END


Text
---------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------

/* Atomic claim - a duplicate transport message must not execute twice. */
CREATE PROCEDURE dbo.API_Config_Execution_Claim
    @ExecutionId INT,
    @Claimed     BIT OUTPUT
AS
BEGIN
    SET NOCOUNT ON;
    SET @Claimed = 0;

    UPDATE dbo.API_Config_Exe
cutionRequest
       SET Status = N'Executing'
     WHERE ExecutionId = @ExecutionId AND Status = N'Queued';

    IF @@ROWCOUNT = 1 SET @Claimed = 1;

    SELECT Claimed = @Claimed;
END


Text
---------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------

CREATE PROCEDURE dbo.API_Config_Execution_Complete
    @ExecutionId INT,
    @Status      NVARCHAR(40),
    @Reason      NVARCHAR(1000) = NULL
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;

    BEGIN TRANSACTION;

    UPDATE dbo.API_Config_Executio
nRequest
       SET Status = @Status, CompletedOn = GETDATE()
     WHERE ExecutionId = @ExecutionId;

    IF @Status IN (N'BusinessFailed', N'TechnicalFailed', N'UnknownOutcome')
        INSERT INTO dbo.API_Config_ExecutionException (ExecutionId, Category
, Reason)
        VALUES (@ExecutionId,
                CASE @Status
                    WHEN N'BusinessFailed'  THEN N'BusinessFailure'
                    WHEN N'UnknownOutcome'  THEN N'UnknownOutcome'
                    ELSE N'TechnicalFailure'
      
          END,
                ISNULL(@Reason, N'Execution did not succeed.'));

    EXEC dbo.API_Config_Audit_Insert
        @EventType  = N'EXECUTION_COMPLETED',
        @EntityType = N'ExecutionRequest',
        @EntityId   = @ExecutionId,
        @Det
ailsJson = @Status;

    COMMIT TRANSACTION;
END


Text
---------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------

/* ============================================================================
   HUMAN IN THE LOOP - create review
   ============================================================================ */
CREATE PROCEDURE dbo.API_Config_Execution_CreateReview

    @ConnectorVersionId  INT,
    @ExecutionUid        UNIQUEIDENTIFIER,
    @BusinessKey         NVARCHAR(200)  = NULL,
    @ExecutionMode       NVARCHAR(30),
    @ResolvedMethod      NVARCHAR(10),
    @ResolvedUrl         NVARCHAR(1000),
    @ResolvedP
ayloadJson NVARCHAR(MAX)  = NULL,
    @SourceDataJson      NVARCHAR(MAX)  = NULL,
    @ActorName           NVARCHAR(128)  = NULL,
    @ExecutionId         INT OUTPUT
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;

    DECLARE @connectorId INT, @confi
gHash CHAR(64), @status NVARCHAR(20),
            @modeAllowed NVARCHAR(30), @isActive BIT;

    SELECT @connectorId = v.ConnectorId,
           @configHash  = v.ConfigHash,
           @status      = v.Status,
           @modeAllowed = v.ExecutionModeAllo
wed,
           @isActive    = c.Enabled
    FROM dbo.API_Config_ConnectorVersion v
    INNER JOIN dbo.API_Config_Connector c ON c.ConnectorId = v.ConnectorId
    WHERE v.ConnectorVersionId = @ConnectorVersionId;

    IF @connectorId IS NULL
    BEGIN
   
     RAISERROR (N'API_Config: connector version not found.', 16, 1);
        RETURN;
    END
    IF @status <> N'Published' OR @isActive = 0
    BEGIN
        RAISERROR (N'API_Config: only an active, published connector version can create a review.', 16, 
1);
        RETURN;
    END
    IF (@modeAllowed = N'AttendedOnly' AND @ExecutionMode <> N'Attended')
       OR (@modeAllowed = N'UnattendedOnly' AND @ExecutionMode <> N'UnattendedAfterApproval')
    BEGIN
        RAISERROR (N'API_Config: the requested ex
ecution mode is not permitted by this connector version.', 16, 1);
        RETURN;
    END

    /* payload hash freezes exactly what the approver will see */
    DECLARE @payloadHash CHAR(64) = CONVERT(CHAR(64),
        HASHBYTES('SHA2_256',
            C
ONVERT(NVARCHAR(MAX), @ResolvedMethod) + N'|' +
            CONVERT(NVARCHAR(MAX), @ResolvedUrl)    + N'|' +
            ISNULL(@ResolvedPayloadJson, N'')), 2);

    BEGIN TRY
        BEGIN TRANSACTION;

        INSERT INTO dbo.API_Config_ExecutionRequest

            (ExecutionUid, ConnectorId, ConnectorVersionId, BusinessKey, ExecutionMode, Status,
             ResolvedMethod, ResolvedUrl, ResolvedPayloadJson, SourceDataJson, PayloadHash, ConfigHash, CreatedBy)
        VALUES
            (@ExecutionUid, 
@connectorId, @ConnectorVersionId, @BusinessKey, @ExecutionMode, N'PendingReview',
             @ResolvedMethod, @ResolvedUrl, @ResolvedPayloadJson, @SourceDataJson, @payloadHash, @configHash, @ActorName);

        SET @ExecutionId = CONVERT(INT, SCOPE_ID
ENTITY());

        INSERT INTO dbo.API_Config_ExecutionApproval (ExecutionId, Status)
        VALUES (@ExecutionId, N'Pending');

        EXEC dbo.API_Config_Audit_Insert
            @EventType  = N'EXECUTION_REVIEW_CREATED',
            @EntityType = N'
ExecutionRequest',
            @EntityId   = @ExecutionId,
            @ActorName  = @ActorName;

        COMMIT TRANSACTION;
    END TRY
    BEGIN CATCH
        IF XACT_STATE() <> 0 ROLLBACK TRANSACTION;
        THROW;
    END CATCH

    SELECT Execution
Id = @ExecutionId, ExecutionUid = @ExecutionUid,
           PayloadHash = @payloadHash, Status = N'PendingReview';
END


Text
---------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------

/* ============================================================================
   EXECUTION AUDIT SEARCH
   ============================================================================ */
CREATE PROCEDURE dbo.API_Config_Execution_GetAudit
    @FromDate 
DATETIME   = NULL,
    @ToDate   DATETIME   = NULL,
    @Status  NVARCHAR(40)   = NULL,
    @Search  NVARCHAR(200)  = NULL,
    @Top     INT            = 200
AS
BEGIN
    SET NOCOUNT ON;

    SELECT TOP (@Top)
        e.ExecutionId, e.ExecutionUid, e.Busi
nessKey, e.ExecutionMode, e.Status,
        e.ResolvedMethod, e.ResolvedUrl, e.PayloadHash, e.ConfigHash,
        e.CreatedOn, e.CompletedOn,
        c.ConnectorName, c.ConnectorCode, c.OperationCode,
        v.VersionNumber,
        cu.CustomerName, a.Ap
plicationName,
        ap.ApprovalId, ap.Status AS ApprovalStatus, ap.ApprovalHash, ap.DecidedBy, ap.DecidedOn,
        AttemptCount   = (SELECT COUNT(*) FROM dbo.API_Config_ExecutionAttempt t WHERE t.ExecutionId = e.ExecutionId),
        LastStatusCode =
 (SELECT TOP (1) t.HttpStatusCode FROM dbo.API_Config_ExecutionAttempt t
                          WHERE t.ExecutionId = e.ExecutionId ORDER BY t.AttemptNumber DESC)
    FROM dbo.API_Config_ExecutionRequest e
    INNER JOIN dbo.API_Config_ExecutionApprova
l ap ON ap.ExecutionId = e.ExecutionId
    INNER JOIN dbo.API_Config_Connector c          ON c.ConnectorId = e.ConnectorId
    INNER JOIN dbo.API_Config_ConnectorVersion v   ON v.ConnectorVersionId = e.ConnectorVersionId
    INNER JOIN dbo.API_Config_Cust
omer cu          ON cu.CustomerId = c.CustomerId
    INNER JOIN dbo.API_Config_Application a        ON a.ApplicationId = c.ApplicationId
    WHERE (@FromDate IS NULL OR e.CreatedOn >= @FromDate)
      AND (@ToDate   IS NULL OR e.CreatedOn <  @ToDate)
    
  AND (@Status  IS NULL OR @Status = N'' OR e.Status = @Status)
      AND (@Search  IS NULL OR @Search = N''
           OR e.BusinessKey LIKE N'%' + @Search + N'%'
           OR c.ConnectorName LIKE N'%' + @Search + N'%'
           OR CONVERT(NVARCHAR(50)
, e.ExecutionId) LIKE N'%' + @Search + N'%'
           OR CONVERT(NVARCHAR(50), e.ExecutionUid) LIKE N'%' + @Search + N'%')
    ORDER BY e.CreatedOn DESC;
END


Text
---------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------

CREATE PROCEDURE dbo.API_Config_Execution_GetById
    @ExecutionId INT
AS
BEGIN
    SET NOCOUNT ON;

    /* 0 - the frozen package */
    SELECT
        e.ExecutionId, e.ExecutionUid, e.BusinessKey, e.ExecutionMode, e.Status,
        e.ResolvedMethod, e.
ResolvedUrl, e.ResolvedPayloadJson, e.SourceDataJson,
        e.PayloadHash, e.ConfigHash, e.CreatedOn, e.CreatedBy, e.CompletedOn,
        c.ConnectorId, c.ConnectorName, c.ConnectorCode, c.OperationCode,
        v.ConnectorVersionId, v.VersionNumber, v.
TimeoutSeconds, v.MaxAttempts,
        v.IdempotencyHeader, v.CorrelationHeader, v.ResponseType, v.IsRetrySafe,
        v.PublishedSnapshotJson,
        cu.CustomerName, a.ApplicationName,
        ap.ApprovalId, ap.Status AS ApprovalStatus, ap.ApprovalHas
h, ap.HashScheme,
        ap.DecidedOn, ap.DecidedBy, ap.DecisionComment
    FROM dbo.API_Config_ExecutionRequest e
    INNER JOIN dbo.API_Config_ExecutionApproval ap ON ap.ExecutionId = e.ExecutionId
    INNER JOIN dbo.API_Config_Connector c          ON 
c.ConnectorId = e.ConnectorId
    INNER JOIN dbo.API_Config_ConnectorVersion v   ON v.ConnectorVersionId = e.ConnectorVersionId
    INNER JOIN dbo.API_Config_Customer cu          ON cu.CustomerId = c.CustomerId
    INNER JOIN dbo.API_Config_Application a 
       ON a.ApplicationId = c.ApplicationId
    WHERE e.ExecutionId = @ExecutionId;

    /* 1 - attempts */
    SELECT AttemptNumber, StartedOn, CompletedOn, HttpStatusCode, Outcome,
           DurationMs, ResponseSnippet, ErrorMessage
    FROM dbo.API_Co
nfig_ExecutionAttempt
    WHERE ExecutionId = @ExecutionId
    ORDER BY AttemptNumber;

    /* 2 - headers of the published version */
    SELECT h.HeaderName, h.HeaderValueTemplate
    FROM dbo.API_Config_Header h
    INNER JOIN dbo.API_Config_ExecutionR
equest e ON e.ConnectorVersionId = h.ConnectorVersionId
    AND h.Enabled = 1
    WHERE e.ExecutionId = @ExecutionId
    ORDER BY h.SortOrder;

    /* 3 - success codes */
    SELECT s.StatusCode
    FROM dbo.API_Config_SuccessCode s
    INNER JOIN dbo.AP
I_Config_ExecutionRequest e ON e.ConnectorVersionId = s.ConnectorVersionId
                                                AND s.Enabled = 1
    WHERE e.ExecutionId = @ExecutionId
    ORDER BY s.StatusCode;

    /* 4 - exceptions */
    SELECT ExceptionId
, Category, Reason, Status, CreatedOn, ResolvedOn, ResolvedBy
    FROM dbo.API_Config_ExecutionException
    WHERE ExecutionId = @ExecutionId
    ORDER BY CreatedOn DESC;
END


Text
---------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------

CREATE PROCEDURE dbo.API_Config_Execution_GetPending
AS
BEGIN
    SET NOCOUNT ON;
    SELECT
        e.ExecutionId, e.ExecutionUid, e.BusinessKey, e.ExecutionMode, e.Status,
        e.ResolvedMethod, e.ResolvedUrl, e.PayloadHash, e.CreatedOn,
        c.C
onnectorName, c.ConnectorCode, c.OperationCode,
        v.VersionNumber, v.ConfigHash,
        cu.CustomerName, a.ApplicationName,
        ap.ApprovalId, ap.Status AS ApprovalStatus
    FROM dbo.API_Config_ExecutionRequest e
    INNER JOIN dbo.API_Config_
ExecutionApproval ap ON ap.ExecutionId = e.ExecutionId
    INNER JOIN dbo.API_Config_Connector c          ON c.ConnectorId = e.ConnectorId
    INNER JOIN dbo.API_Config_ConnectorVersion v   ON v.ConnectorVersionId = e.ConnectorVersionId
    INNER JOIN dbo
.API_Config_Customer cu          ON cu.CustomerId = c.CustomerId
    INNER JOIN dbo.API_Config_Application a        ON a.ApplicationId = c.ApplicationId
    WHERE ap.Status = N'Pending'
    ORDER BY e.CreatedOn;
END


Text
---------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------

/* ----------------------------------------------------------------------------
   ATTENDED EXECUTE - status change and outbox row in one transaction
   ---------------------------------------------------------------------------- */
CREATE PROCEDURE dbo.
API_Config_Execution_Queue
    @ExecutionId INT,
    @ActorName   NVARCHAR(128) = NULL
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;

    DECLARE @approvalStatus NVARCHAR(20), @storedHash CHAR(64), @recomputed CHAR(64);

    SELECT @approvalStatus =
 ap.Status,
           @storedHash     = ap.ApprovalHash,
           @recomputed     = CONVERT(CHAR(64), HASHBYTES('SHA2_256',
                   CONVERT(NVARCHAR(MAX), e.ExecutionId)    + N'|' +
                   CONVERT(NVARCHAR(MAX), e.ResolvedMethod)
 + N'|' +
                   CONVERT(NVARCHAR(MAX), e.ResolvedUrl)    + N'|' +
                   ISNULL(e.ResolvedPayloadJson, N'')       + N'|' +
                   ISNULL(e.ConfigHash, N'')), 2)
    FROM dbo.API_Config_ExecutionApproval ap
    INNER JO
IN dbo.API_Config_ExecutionRequest e ON e.ExecutionId = ap.ExecutionId
    WHERE ap.ExecutionId = @ExecutionId;

    IF @approvalStatus <> N'Approved'
    BEGIN
        RAISERROR (N'API_Config: the execution is not approved.', 16, 1);
        RETURN;
    
END

    /* fail closed on tampering */
    IF @storedHash IS NULL OR @storedHash <> @recomputed
    BEGIN
        UPDATE dbo.API_Config_ExecutionApproval SET Status = N'Invalidated' WHERE ExecutionId = @ExecutionId;
        UPDATE dbo.API_Config_Executio
nRequest  SET Status = N'ApprovalInvalidated' WHERE ExecutionId = @ExecutionId;

        INSERT INTO dbo.API_Config_ExecutionException (ExecutionId, Category, Reason)
        VALUES (@ExecutionId, N'ControlFailure',
                N'Approval hash mismatc
h. The package changed after approval, so execution was blocked.');

        EXEC dbo.API_Config_Audit_Insert
            @EventType  = N'EXECUTION_HASH_MISMATCH',
            @EntityType = N'ExecutionRequest',
            @EntityId   = @ExecutionId,
    
        @ActorName  = @ActorName;

        RAISERROR (N'API_Config: approval hash mismatch. Execution blocked and approval invalidated.', 16, 1);
        RETURN;
    END

    BEGIN TRY
        BEGIN TRANSACTION;

        UPDATE dbo.API_Config_ExecutionReq
uest
           SET Status = N'Queued'
         WHERE ExecutionId = @ExecutionId AND Status = N'ApprovedAwaitingExecution';

        IF @@ROWCOUNT = 0
        BEGIN
            RAISERROR (N'API_Config: the execution is not awaiting execution.', 16, 1);
  
          ROLLBACK TRANSACTION;
            RETURN;
        END

        INSERT INTO dbo.API_Config_OutboxMessage (ExecutionId, MessageType, PayloadJson)
        VALUES (@ExecutionId, N'ExecuteApprovedPackage',
                N'{"executionId":' + CONVERT
(NVARCHAR(20), @ExecutionId) + N'}');

        EXEC dbo.API_Config_Audit_Insert
            @EventType  = N'EXECUTION_QUEUED',
            @EntityType = N'ExecutionRequest',
            @EntityId   = @ExecutionId,
            @ActorName  = @ActorName;

  
      COMMIT TRANSACTION;
    END TRY
    BEGIN CATCH
        IF XACT_STATE() <> 0 ROLLBACK TRANSACTION;
        THROW;
    END CATCH
END


Text
---------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------

CREATE PROCEDURE dbo.API_Config_Execution_RecordAttempt
    @ExecutionId     INT,
    @HttpStatusCode  INT            = NULL,
    @Outcome         NVARCHAR(40),
    @DurationMs      INT            = NULL,
    @ResponseSnippet NVARCHAR(4000) = NULL,
    @
ErrorMessage    NVARCHAR(2000) = NULL
AS
BEGIN
    SET NOCOUNT ON;
    DECLARE @attemptNo INT;

    SELECT @attemptNo = ISNULL(MAX(AttemptNumber), 0) + 1
    FROM dbo.API_Config_ExecutionAttempt WHERE ExecutionId = @ExecutionId;

    INSERT INTO dbo.API_C
onfig_ExecutionAttempt
        (ExecutionId, AttemptNumber, CompletedOn, HttpStatusCode, Outcome, DurationMs, ResponseSnippet, ErrorMessage)
    VALUES
        (@ExecutionId, @attemptNo, GETDATE(), @HttpStatusCode, @Outcome, @DurationMs, @ResponseSnippet,
 @ErrorMessage);

    SELECT AttemptNumber = @attemptNo;
END


Text
---------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------

CREATE PROCEDURE dbo.API_Config_Execution_Reject
    @ExecutionId INT,
    @ActorName   NVARCHAR(128)  = NULL,
    @Comment     NVARCHAR(1000) = NULL
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;

    BEGIN TRY
        BEGIN TRANSACTION;

        U
PDATE dbo.API_Config_ExecutionApproval
           SET Status          = N'Rejected',
               DecidedOn    = GETDATE(),
               DecidedBy       = @ActorName,
               DecisionComment = @Comment
         WHERE ExecutionId = @ExecutionId 
AND Status = N'Pending';

        IF @@ROWCOUNT = 0
        BEGIN
            RAISERROR (N'API_Config: only a Pending approval can be rejected.', 16, 1);
            ROLLBACK TRANSACTION;
            RETURN;
        END

        UPDATE dbo.API_Config_Exec
utionRequest
           SET Status = N'Rejected', CompletedOn = GETDATE()
         WHERE ExecutionId = @ExecutionId;

        EXEC dbo.API_Config_Audit_Insert
            @EventType  = N'EXECUTION_REJECTED',
            @EntityType = N'ExecutionRequest',

            @EntityId   = @ExecutionId,
            @ActorName  = @ActorName;

        COMMIT TRANSACTION;
    END TRY
    BEGIN CATCH
        IF XACT_STATE() <> 0 ROLLBACK TRANSACTION;
        THROW;
    END CATCH
END


Text
---------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------

/* ----------------------------------------------------------------------------
   DEV/TEST ONLY - simulate a post-approval data change so the hash check fires
   ---------------------------------------------------------------------------- */
CREATE PROC
EDURE dbo.API_Config_Execution_SimulateTamper
    @ExecutionId INT
AS
BEGIN
    SET NOCOUNT ON;
    UPDATE dbo.API_Config_ExecutionRequest
       SET ResolvedPayloadJson = JSON_MODIFY(ISNULL(ResolvedPayloadJson, N'{}'),
                                   
          N'$.tamperedOn',
                                             CONVERT(NVARCHAR(40), GETDATE(), 126))
     WHERE ExecutionId = @ExecutionId;

    EXEC dbo.API_Config_Audit_Insert
        @EventType  = N'EXECUTION_SIMULATED_DATA_CHANGE',
        @
EntityType = N'ExecutionRequest',
        @EntityId   = @ExecutionId;
END


Text
---------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------

/* ----------------------------------------------------------------------------
   WORKER SIDE
   ---------------------------------------------------------------------------- */
CREATE PROCEDURE dbo.API_Config_Outbox_GetPending
    @Top INT = 10
AS
BEGIN

    SET NOCOUNT ON;
    SELECT TOP (@Top) OutboxId, ExecutionId, MessageType, PayloadJson, Attempts, CreatedOn
    FROM dbo.API_Config_OutboxMessage WITH (READPAST)
    WHERE Status = N'Pending'
    ORDER BY OutboxId;
END


Text
---------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------

CREATE PROCEDURE dbo.API_Config_Outbox_MarkPublished
    @OutboxId  INT,
    @Succeeded BIT,
    @Error     NVARCHAR(2000) = NULL
AS
BEGIN
    SET NOCOUNT ON;
    UPDATE dbo.API_Config_OutboxMessage
       SET Status         = CASE WHEN @Succeeded = 1 TH
EN N'Published' ELSE N'Failed' END,
           PublishedOn = CASE WHEN @Succeeded = 1 THEN GETDATE() ELSE NULL END,
           Attempts       = Attempts + 1,
           LastError      = @Error
     WHERE OutboxId = @OutboxId;
END


Text
---------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------

CREATE PROCEDURE dbo.API_Config_Provider_GetList
AS
BEGIN
    SET NOCOUNT ON;
    SELECT ProviderId, ProviderCategory, ProviderAlias, AdapterType, SettingsJson, Enabled
    FROM dbo.API_Config_Provider
    ORDER BY ProviderCategory, ProviderAlias;
END


Text
---------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------

CREATE   PROCEDURE dbo.APS_Agent_Delete
    @agid     smallint,
    @actionby varchar(20)
AS
BEGIN
    SET NOCOUNT ON;
    UPDATE dbo.APS_Agent
    SET    activestatus = 0, modifiedby = @actionby, modifiedon = GETDATE()
    WHERE  agid = @agid;
END


Text
---------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------

/* =============================================================================
   8. AGENTS
   ============================================================================= */
CREATE   PROCEDURE dbo.APS_Agent_Get
AS
BEGIN
    SET NOCOUNT ON;
    SELECT
 a.agid, a.agentname, a.purpose, a.apiendpoint, a.apiparams,
           a.scheduletype, a.scheduletime, a.everyminutes, a.activestatus,
           a.modifiedby, a.modifiedon,
           CASE WHEN a.scheduletype = 2
                THEN 'Every ' + CAST(a.e
veryminutes AS varchar(3)) + ' minutes'
                ELSE 'Daily at ' + ISNULL(a.scheduletime, '--:--') END AS scheduletext,
           e.startedon       AS lastexecution,
           e.status          AS laststatus,
           e.durationseconds AS last
duration,
           e.recordcount     AS lastrecordcount
    FROM   dbo.APS_Agent a
    OUTER APPLY (SELECT TOP 1 x.startedon, x.status, x.durationseconds, x.recordcount
                 FROM   dbo.APS_AgentExecution x
                 WHERE  x.agid = a.
agid
                 ORDER BY x.aeid DESC) e
    ORDER BY a.agentname;
END


Text
---------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------

CREATE   PROCEDURE dbo.APS_Agent_GetById
    @agid smallint
AS
BEGIN
    SET NOCOUNT ON;
    SELECT agid, agentname, purpose, apiendpoint, apiparams, scheduletype,
           scheduletime, everyminutes, activestatus, modifiedby, modifiedon
    FROM   dbo
.APS_Agent WHERE agid = @agid;
END


Text
---------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------

CREATE   PROCEDURE dbo.APS_Agent_Save
    @agid         smallint,          -- 0 to insert
    @agentname    nvarchar(200),
    @purpose      nvarchar(1000),
    @apiendpoint  nvarchar(1000),
    @apiparams    nvarchar(MAX) = NULL,
    @scheduletype tinyi
nt,
    @scheduletime varchar(5) = NULL,
    @everyminutes tinyint = NULL,
    @activestatus tinyint = 1,
    @actionby     varchar(20),
    @newagid      smallint OUTPUT,
    @message      nvarchar(200) OUTPUT
AS
BEGIN
    SET NOCOUNT ON;
    SET @newagi
d = 0;

    IF @agentname IS NULL OR LTRIM(RTRIM(@agentname)) = ''
    BEGIN
        SET @message = 'Agent name is required.';
        RETURN;
    END
    IF @scheduletype = 2 AND (@everyminutes IS NULL OR @everyminutes <= 0)
    BEGIN
        SET @messag
e = 'Frequency in minutes must be a positive number.';
        RETURN;
    END
    IF EXISTS (SELECT 1 FROM dbo.APS_Agent WHERE agentname = @agentname AND agid <> @agid)
    BEGIN
        SET @message = 'An agent with this name already exists.';
        R
ETURN;
    END

    IF @agid = 0
    BEGIN
        INSERT INTO dbo.APS_Agent (agentname, purpose, apiendpoint, apiparams, scheduletype,
                                   scheduletime, everyminutes, activestatus, modifiedby, modifiedon)
        VALUES (@a
gentname, @purpose, @apiendpoint, @apiparams, @scheduletype,
                @scheduletime, @everyminutes, @activestatus, @actionby, GETDATE());
        SET @newagid = CAST(SCOPE_IDENTITY() AS smallint);
        SET @message = 'Agent scheduled.';
    END

    ELSE
    BEGIN
        UPDATE dbo.APS_Agent
        SET    agentname = @agentname, purpose = @purpose, apiendpoint = @apiendpoint,
               apiparams = @apiparams, scheduletype = @scheduletype, scheduletime = @scheduletime,
               everym
inutes = @everyminutes, activestatus = @activestatus,
               modifiedby = @actionby, modifiedon = GETDATE()
        WHERE  agid = @agid;
        SET @newagid = @agid;
        SET @message = 'Agent updated.';
    END
END


Text
---------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------

CREATE   PROCEDURE dbo.APS_AgentExecution_Get
    @agid smallint = NULL,
    @top  int = 50
AS
BEGIN
    SET NOCOUNT ON;
    SELECT TOP (@top) e.aeid, e.agid, a.agentname, e.startedon, e.durationseconds,
           e.status, e.recordcount, e.message
    
FROM   dbo.APS_AgentExecution e
    JOIN   dbo.APS_Agent a ON a.agid = e.agid
    WHERE (@agid IS NULL OR e.agid = @agid)
    ORDER BY e.aeid DESC;
END


Text
---------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------

CREATE   PROCEDURE dbo.APS_AgentExecution_Log
    @agid            smallint,
    @startedon       datetime,
    @durationseconds int,
    @status          varchar(30),
    @recordcount     int = NULL,
    @message         nvarchar(2000) = NULL
AS
BEGIN
 
   SET NOCOUNT ON;
    INSERT INTO dbo.APS_AgentExecution (agid, startedon, durationseconds, status, recordcount, message)
    VALUES (@agid, @startedon, @durationseconds, @status, @recordcount, @message);
END


Text
---------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------
CREATE PROCEDURE dbo.APS_AgentRunningStatus
    @Status VARCHAR(10) = NULL
AS
BEGIN
    SET NOCOUNT ON;

    SELECT *
    FROM APS_AgentExecution
    WHERE @Status IS NULL
       OR Status = @Status;
END;

Text
---------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------
CREATE PROCEDURE dbo.APS_ChecklistQuestionMaster_Delete
(
    @QuestionID INT
)
AS
BEGIN
    SET NOCOUNT ON;

    BEGIN TRY

        -- Check if record exists
        IF NOT EXISTS
        (
            SELECT 1
            FROM dbo.APS_ChecklistQuestionMaster
            WHERE QuestionID = @QuestionID
        )
        BEGIN
            RAISERROR ('Checklist Question record not found.', 16, 1);
            RETURN;
        END

        -- Delete the record
        UPDATE dbo.APS_ChecklistQuestionMaster  SET Status = 0
        WHERE QuestionID = @QuestionID

        SELECT
            1 AS Success,
            'Checklist Question deleted successfully.' AS Message;

    END TRY
    BEGIN CATCH

        SELECT
            0 AS Success,
            ERROR_MESSAGE() AS Message;

    END CATCH
END;

Text
---------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------
CREATE PROCEDURE APS_ChecklistQuestionMaster_Get  
AS    
BEGIN    
    SET NOCOUNT ON;    
    
    SELECT *    
    FROM dbo.APS_ChecklistQuestionMaster WHERE Status = 1    
END; 

Text
---------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------
CREATE PROCEDURE APS_ChecklistQuestionMaster_GetById
    @QuestionId INT = NULL  
AS  
BEGIN  
    SET NOCOUNT ON;  
  
    SELECT *  
    FROM dbo.APS_ChecklistQuestionMaster  
    WHERE (QuestionId IS NULL OR QuestionId = @QuestionId);  
END; 

Text
---------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------
CREATE PROCEDURE APS_ChecklistQuestionMaster_Save  
(  
    @QuestionId            INT = NULL,  
    @Question              VARCHAR(1000),  
    @Category              VARCHAR(100) = NULL,  
    @Status                VARCHAR(50) = NULL,  
    @SourceApplicability   VARCHAR(255) = NULL,  
    @InstructionPrompt     VARCHAR(4000) = NULL,  
    @BusinessRulesPrompt   VARCHAR(4000) = NULL,  
    @OutputType            VARCHAR(100) = NULL,  
    @DisplayOrder          INT = NULL,  
    @DeveloperNotes        VARCHAR(2000) = NULL  
)  
AS  
BEGIN  
    SET NOCOUNT ON;  
  
    -- INSERT  
    IF ISNULL(@QuestionId, 0) = 0  
    BEGIN  
        INSERT INTO APS_ChecklistQuestionMaster  
        (  
            Question,  
            Category,  
            Status,  
            SourceApplicability,  
            InstructionPrompt,  
            BusinessRulesPrompt,  
            OutputType,  
            DisplayOrder,  
            DeveloperNotes  
        )  
        VALUES  
        (  
            @Question,  
            @Category,  
            @Status,  
            @SourceApplicability,  
            @InstructionPrompt,  
            @BusinessRulesPrompt,  
            @OutputType,  
            @DisplayOrder,  
            @DeveloperNotes  
        );  
  
        SELECT SCOPE_IDENTITY() AS QuestionId,  
               'Inserted Successfully' AS Message;  
    END  
    ELSE  
    BEGIN  
        -- Validate QuestionId exists  
        IF NOT EXISTS  
        (  
            SELECT 1  
            FROM APS_ChecklistQuestionMaster  
            WHERE QuestionId = @QuestionId  
        )  
        BEGIN  
            RAISERROR('QuestionId does not exist.', 16, 1);  
            RETURN;  
        END;  
  
        -- UPDATE  
        UPDATE APS_ChecklistQuestionMaster  
        SET  
            Question = @Question,  
            Category = @Category,  
            Status = @Status,  
            SourceApplicability = @SourceApplicability,  
            InstructionPrompt = @InstructionPrompt,  
            BusinessRulesPrompt = @BusinessRulesPrompt,  
            OutputType = @OutputType,  
            DisplayOrder = @DisplayOrder,  
            DeveloperNotes = @DeveloperNotes  
        WHERE QuestionId = @QuestionId;  
  
        SELECT @QuestionId AS QuestionId,  
               'Updated Successfully' AS Message;  
    END  
END; 

Text
---------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------

/* =============================================================================
   MASTERS AND LOOKUPS
   ============================================================================= */

CREATE   PROCEDURE dbo.APS_ClientMaster_Get
    @ActiveOnly tinyi
nt = 1
AS
BEGIN
    SET NOCOUNT ON;
    SELECT ClientID, ClientCode, ClientName, IsActive
    FROM   dbo.APS_ClientMaster
    WHERE  (@ActiveOnly = 0 OR IsActive = 1)
    ORDER BY ClientName;
END


Text
---------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------

/* =============================================================================
   COLLECTIONS
   ============================================================================= */

CREATE   PROCEDURE dbo.APS_Collection_Get
    @CollectionID int = NULL
AS

BEGIN
    SET NOCOUNT ON;
    SELECT c.CollectionID, c.CollectionName, c.TableName, c.Description,
           c.UseCaseID, u.UseCaseCode, c.IsGenerated, c.GeneratedOn,
           c.CreatedBy, c.CreatedDate,
           ColumnCount = (SELECT COUNT(*) FROM 
dbo.APS_CollectionColumn cc
                          WHERE cc.CollectionID = c.CollectionID AND cc.IsActive = 1)
    FROM   dbo.APS_CollectionMaster c
    LEFT   JOIN dbo.APS_UseCaseMaster u ON u.UseCaseID = c.UseCaseID
    WHERE  c.IsActive = 1
      AN
D (@CollectionID IS NULL OR c.CollectionID = @CollectionID)
    ORDER BY c.CollectionName;
END


Text
---------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------

CREATE   PROCEDURE dbo.APS_Collection_MarkGenerated
    @CollectionID int,
    @ActionBy     varchar(30)
AS
BEGIN
    SET NOCOUNT ON;
    UPDATE dbo.APS_CollectionMaster
       SET IsGenerated = 1, GeneratedOn = GETDATE(),
           ModifiedBy = @Action
By, ModifiedDate = GETDATE()
     WHERE CollectionID = @CollectionID;
END


Text
---------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------

/* --- Save a collection definition -----------------------------------------
   Definition only. No DDL runs here: generating the table is a separate,
   explicit step, so a mistyped column can be corrected before it becomes a
   real object. */
CREATE 
  PROCEDURE dbo.APS_Collection_Save
    @CollectionID   int,
    @CollectionName varchar(128),
    @TableName      varchar(128),
    @Description    varchar(1000) = NULL,
    @UseCaseID      int = NULL,
    @Columns        dbo.APS_CollectionColumnType REA
DONLY,
    @ActionBy       varchar(30),
    @NewCollectionID int OUTPUT,
    @Message        varchar(200) OUTPUT
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;
    SET @NewCollectionID = 0;

    IF LEN(LTRIM(RTRIM(ISNULL(@CollectionName, '')))) = 0
 
   BEGIN
        SET @Message = 'Enter a collection name.';
        RETURN;
    END

    -- Every generated object carries the prefix, so a collection can never
    -- create a table that looks like it belongs to another system.
    IF LEFT(@TableName, 6)
 <> 'APS_'
        SET @TableName = 'APS_' + @TableName;

    IF EXISTS (SELECT 1 FROM dbo.APS_CollectionMaster
               WHERE TableName = @TableName AND CollectionID <> @CollectionID)
    BEGIN
        SET @Message = 'A collection already uses that
 table name.';
        RETURN;
    END

    IF NOT EXISTS (SELECT 1 FROM @Columns WHERE IsIdentityColumn = 1)
    BEGIN
        SET @Message = 'The first column must be an identity primary key.';
        RETURN;
    END

    IF EXISTS (SELECT 1 FROM @Colu
mns
               WHERE ConstraintType = 'FK'
                 AND (RefTableName IS NULL OR RefColumnName IS NULL
                      OR LEN(LTRIM(RTRIM(RefTableName))) = 0
                      OR LEN(LTRIM(RTRIM(RefColumnName))) = 0))
    BEGIN
     
   SET @Message = 'A foreign key column needs both a reference table and a reference column.';
        RETURN;
    END

    BEGIN TRY
        BEGIN TRANSACTION;

        IF @CollectionID = 0
        BEGIN
            INSERT INTO dbo.APS_CollectionMaster
 
               (CollectionName, TableName, Description, UseCaseID, CreatedBy)
            VALUES (@CollectionName, @TableName, @Description, @UseCaseID, @ActionBy);

            SET @NewCollectionID = CAST(SCOPE_IDENTITY() AS int);
        END
        ELS
E
        BEGIN
            UPDATE dbo.APS_CollectionMaster
               SET CollectionName = @CollectionName, TableName = @TableName,
                   Description = @Description, UseCaseID = @UseCaseID,
                   ModifiedBy = @ActionBy, Modi
fiedDate = GETDATE()
             WHERE CollectionID = @CollectionID;

            SET @NewCollectionID = @CollectionID;
        END

        -- Low-volume definition rows: delete and re-insert is simpler and safer
        -- than diff logic, and it runs 
inside the same transaction.
        DELETE FROM dbo.APS_CollectionColumn WHERE CollectionID = @NewCollectionID;

        INSERT INTO dbo.APS_CollectionColumn
            (CollectionID, ColumnName, DataTypeName, ColumnLength, NumericPrecision, NumericScal
e,
             IsNullable, ConstraintType, DefaultValue, CheckExpression, RefTableName, RefColumnName,
             IsIdentityColumn, IsSystemColumn, SortOrder, CreatedBy)
        SELECT @NewCollectionID, ColumnName, DataTypeName, ColumnLength, NumericPr
ecision, NumericScale,
               IsNullable, ConstraintType, DefaultValue, CheckExpression, RefTableName, RefColumnName,
               IsIdentityColumn, IsSystemColumn, SortOrder, @ActionBy
        FROM   @Columns;

        EXEC dbo.APS_ConfigHistor
y_Insert
             @UseCaseID = @UseCaseID,
             @ConfigItem = 'Collection',
             @Detail = @TableName,
             @ActionBy = @ActionBy;

        COMMIT TRANSACTION;
    END TRY
    BEGIN CATCH
        IF XACT_STATE() <> 0 ROLLBACK T
RANSACTION;
        SET @NewCollectionID = 0;
        SET @Message = 'The collection could not be saved.';
        RETURN;
    END CATCH

    SET @Message = 'Collection saved.';
END


Text
---------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------

CREATE   PROCEDURE dbo.APS_CollectionColumn_Get
    @CollectionID int
AS
BEGIN
    SET NOCOUNT ON;
    SELECT CollectionColumnID, CollectionID, ColumnName, DataTypeName, ColumnLength,
           NumericPrecision, NumericScale, IsNullable, ConstraintType,
 DefaultValue,
           CheckExpression, RefTableName, RefColumnName, IsIdentityColumn,
           IsSystemColumn, SortOrder
    FROM   dbo.APS_CollectionColumn
    WHERE  CollectionID = @CollectionID AND IsActive = 1
    ORDER BY SortOrder;
END


Text
---------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------

/* Header values are never returned here. The UI renders a mask and calls
   APS_ConfigHeader_GetSecret only when the user explicitly reveals one. */
CREATE   PROCEDURE dbo.APS_ConfigHeader_Get
    @cid smallint
AS
BEGIN
    SET NOCOUNT ON;
    SELECT ch
id, headerkey,
           CASE WHEN headervalue IS NULL OR headervalue = '' THEN ''
                ELSE REPLICATE('*', 10) END AS maskedvalue,
           sortorder
    FROM   dbo.APS_ConfigHeader
    WHERE  cid = @cid
    ORDER BY sortorder, chid;
END


Text
---------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------

CREATE   PROCEDURE dbo.APS_ConfigHeader_GetSecret
    @chid smallint
AS
BEGIN
    SET NOCOUNT ON;
    SELECT chid, headerkey, headervalue
    FROM   dbo.APS_ConfigHeader
    WHERE  chid = @chid;
END


Text
---------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------

CREATE   PROCEDURE dbo.APS_ConfigHistory_Get
    @UseCaseID int = NULL,
    @Top       int = 10
AS
BEGIN
    SET NOCOUNT ON;
    SELECT TOP (@Top) h.ConfigHistoryID, h.UseCaseID, u.UseCaseCode,
           h.ConfigItem, h.Detail, h.CreatedBy, h.CreatedDat
e
    FROM   dbo.APS_ConfigHistory h
    LEFT   JOIN dbo.APS_UseCaseMaster u ON u.UseCaseID = h.UseCaseID
    WHERE  (@UseCaseID IS NULL OR h.UseCaseID = @UseCaseID)
    ORDER BY h.ConfigHistoryID DESC;
END


Text
---------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------

CREATE   PROCEDURE dbo.APS_ConfigHistory_Insert
    @UseCaseID  int = NULL,
    @ConfigItem varchar(100),
    @Detail     varchar(1000) = NULL,
    @ActionBy   varchar(30)
AS
BEGIN
    SET NOCOUNT ON;
    INSERT INTO dbo.APS_ConfigHistory (UseCaseID, Con
figItem, Detail, CreatedBy)
    VALUES (@UseCaseID, @ConfigItem, @Detail, @ActionBy);
END


Text
---------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------

CREATE   PROCEDURE dbo.APS_Configuration_SetApiStatus
    @cid       smallint,
    @apistatus varchar(20)
AS
BEGIN
    SET NOCOUNT ON;
    UPDATE dbo.APS_Configuration SET apistatus = @apistatus WHERE cid = @cid;
END


Text
---------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------

/* =============================================================================
   7. INBOX DETAIL LAYOUT
   ============================================================================= */
CREATE   PROCEDURE dbo.APS_DetailConfig_Get
AS
BEGIN
    SET NO
COUNT ON;
    SELECT TOP 1 dcid, mode, knowledgeai, allowapprove, allowreject,
           allowclarify, allowcomments, modifiedby, modifiedon
    FROM   dbo.APS_DetailConfig
    ORDER BY dcid DESC;
END


Text
---------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------

/* Replaces the whole detail layout in one transaction. rowkey/parentrowkey in
   the TVPs are client-side identifiers; they are mapped to real dtid values
   here so the caller never has to know database keys. */
CREATE   PROCEDURE [dbo].[APS_DetailConf
ig_Save]
    @mode          tinyint,
    @knowledgeai   bit,
    @allowapprove  bit,
    @allowreject   bit,
    @allowclarify  bit,
    @allowcomments bit,
    @tabs          dbo.APS_DetailTabType   READONLY,
    @fields        dbo.APS_DetailFieldType RE
ADONLY,
    @actionby      varchar(20),
    @success       bit OUTPUT,
    @message       nvarchar(400) OUTPUT
AS
BEGIN
    SET NOCOUNT ON;
    SET @success = 0;

    IF NOT EXISTS (SELECT 1 FROM @fields WHERE rowkey IS NULL AND mode = @mode)
    BEGIN
  
      SET @message = 'Configure at least one header field.';
        RETURN;
    END
    IF NOT EXISTS (SELECT 1 FROM @tabs WHERE parentrowkey IS NULL AND mode = @mode)
    BEGIN
        SET @message = 'Configure at least one primary tab.';
        RETURN
;
    END
    IF EXISTS (SELECT 1 FROM @tabs WHERE label IS NULL OR LTRIM(RTRIM(label)) = '')
    BEGIN
        SET @message = 'Every tab and sub-tab needs a label.';
        RETURN;
    END
    /* A tab with no sub-tabs must carry its own fields. */
    
IF EXISTS (SELECT 1 FROM @tabs t
               WHERE NOT EXISTS (SELECT 1 FROM @tabs s WHERE s.parentrowkey = t.rowkey)
                 AND NOT EXISTS (SELECT 1 FROM @fields f WHERE f.rowkey = t.rowkey))
    BEGIN
        SET @message = 'Every tab and s
ub-tab must have at least one assigned field.';
        RETURN;
    END

    BEGIN TRY
        BEGIN TRANSACTION;

        DECLARE @dcid smallint = (SELECT TOP 1 dcid FROM dbo.APS_DetailConfig ORDER BY dcid DESC);

        IF @dcid IS NULL
        BEGIN
 
           INSERT INTO dbo.APS_DetailConfig (mode, knowledgeai, allowapprove, allowreject,
                                              allowclarify, allowcomments, modifiedby, modifiedon)
            VALUES (@mode, @knowledgeai, @allowapprove, @allowrej
ect,
                    @allowclarify, @allowcomments, @actionby, GETDATE());
            SET @dcid = CAST(SCOPE_IDENTITY() AS smallint);
        END
        ELSE
        BEGIN
            UPDATE dbo.APS_DetailConfig
            SET    mode = @mode, know
ledgeai = @knowledgeai, allowapprove = @allowapprove,
                   allowreject = @allowreject, allowclarify = @allowclarify,
                   allowcomments = @allowcomments, modifiedby = @actionby, modifiedon = GETDATE()
            WHERE  dcid = 
@dcid;
        END

        /* Scoped to the submitted mode only. The other workspace layout stays in
           place, so switching between Data Review and Evidence Review and saving
           does not discard the layout you are not looking at. */
     
   DELETE FROM dbo.APS_DetailField WHERE dcid = @dcid AND mode = @mode;
        DELETE FROM dbo.APS_DetailTab   WHERE dcid = @dcid AND mode = @mode AND parenttabid IS NOT NULL;
        DELETE FROM dbo.APS_DetailTab   WHERE dcid = @dcid AND mode = @mode;


        -- Changed smallint PRIMARY KEY datatype to VARCHAR(MAX)
        --DECLARE @map TABLE (rowkey smallint PRIMARY KEY, dtid smallint);
        DECLARE @map TABLE (rowkey VARCHAR(MAX), dtid smallint);

        /* Primary tabs first so sub-tabs can res
olve their parent. */
        INSERT INTO dbo.APS_DetailTab (dcid, parenttabid, mode, tabkey, label, icon, displaytype, badge, sortorder)
        OUTPUT inserted.dtid, inserted.tabkey INTO @map (dtid, rowkey)
        SELECT @dcid, NULL, t.mode, t.tabkey, 
t.label, t.icon, t.displaytype, t.badge, t.sortorder
        FROM   @tabs t WHERE t.parentrowkey IS NULL;

        /* OUTPUT cannot read the TVP rowkey directly, so re-map by tabkey. */
        DELETE FROM @map;
        INSERT INTO @map (rowkey, dtid)
   
     SELECT t.rowkey, d.dtid
        FROM   @tabs t
        JOIN   dbo.APS_DetailTab d ON d.dcid = @dcid AND d.mode = t.mode
                                  AND d.tabkey = t.tabkey AND d.parenttabid IS NULL
        WHERE  t.parentrowkey IS NULL;

      
  INSERT INTO dbo.APS_DetailTab (dcid, parenttabid, mode, tabkey, label, icon, displaytype, badge, sortorder)
        SELECT @dcid, m.dtid, t.mode, t.tabkey, t.label, t.icon, t.displaytype, t.badge, t.sortorder
        FROM   @tabs t
        JOIN   @map m
 ON m.rowkey = t.parentrowkey
        WHERE  t.parentrowkey IS NOT NULL;

        INSERT INTO @map (rowkey, dtid)
        SELECT t.rowkey, d.dtid
        FROM   @tabs t
        JOIN   @map p ON p.rowkey = t.parentrowkey
        JOIN   dbo.APS_DetailTab d 
ON d.dcid = @dcid AND d.mode = t.mode
                                  AND d.tabkey = t.tabkey AND d.parenttabid = p.dtid
        WHERE  t.parentrowkey IS NOT NULL
          AND  NOT EXISTS (SELECT 1 FROM @map x WHERE x.rowkey = t.rowkey);

        INSER
T INTO dbo.APS_DetailField (dcid, dtid, mode, fieldname, isrequired, sortorder)
        SELECT @dcid, m.dtid, f.mode, f.fieldname, f.isrequired, f.sortorder
        FROM   @fields f
        LEFT JOIN @map m ON m.rowkey = f.rowkey;

        INSERT INTO dbo
.APS_Configuration_History (configitem, detail, actionby, actiondt)
        VALUES ('Detail layout',
                CASE WHEN @mode = 1 THEN 'Data Review Workspace' ELSE 'Evidence Review Workspace' END
                + ' - ' + CAST((SELECT COUNT(*) FROM
 @tabs WHERE parentrowkey IS NULL) AS varchar(10))
                + ' primary tabs', @actionby, GETDATE());

        COMMIT TRANSACTION;
        SET @success = 1;
        SET @message = 'Detail layout saved.';
    END TRY
    BEGIN CATCH
        IF @@TRA
NCOUNT > 0 ROLLBACK TRANSACTION;
        SET @message = ERROR_MESSAGE();
    END CATCH
END


Text
---------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------

CREATE   PROCEDURE dbo.APS_DetailField_Get
    @dcid smallint
AS
BEGIN
    SET NOCOUNT ON;
    SELECT dfid, dcid, dtid, mode, fieldname, isrequired, sortorder
    FROM   dbo.APS_DetailField
    WHERE  dcid = @dcid
    ORDER BY mode, dtid, sortorder, dfid
;
END


Text
---------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------
CREATE PROCEDURE APS_DetailTab_Delete
(
    @dtid INT
)
AS
BEGIN
    SET NOCOUNT ON;

    BEGIN TRY
        BEGIN TRANSACTION;

        -- Delete child records first
        DELETE FROM APS_DetailField
        WHERE dtid = @dtid;

        -- Delete parent record
        DELETE FROM APS_DetailTab
        WHERE dtid = @dtid;

        COMMIT TRANSACTION;
    END TRY
    BEGIN CATCH
        IF @@TRANCOUNT > 0
            ROLLBACK TRANSACTION;

        THROW;
    END CATCH
END;

Text
---------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------

CREATE   PROCEDURE dbo.APS_DetailTab_Get
    @dcid smallint
AS
BEGIN
    SET NOCOUNT ON;
    SELECT dtid, dcid, parenttabid, mode, tabkey, label, icon, displaytype, badge, sortorder
    FROM   dbo.APS_DetailTab
    WHERE  dcid = @dcid
    ORDER BY mode, 
ISNULL(parenttabid, dtid), parenttabid, sortorder, dtid;
END


Text
---------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------

/* =============================================================================
   INBOX SETUP
   ============================================================================= */

CREATE   PROCEDURE [dbo].[APS_InboxConfig_Get]
    @UseCaseID int=1
AS
BEGIN
    SET NOCOUNT ON;

    SELECT InboxConfigID, UseCaseID, GridType, StatusProcedureName
    FROM   dbo.APS_InboxConfig
    WHERE  
    UseCaseID = @UseCaseID AND IsActive = 1;

    SELECT c.InboxConfigID, c.GridType, col.InboxColumnID, col.FieldCode,
           col.DisplayName, col.SortOrder
    FROM   dbo.APS_InboxColumn col
    INNER  JOIN dbo.APS_InboxConfig c ON c.InboxConfigID = col.InboxConfigID
    WHERE  c.UseCaseID = @UseCaseID AND c.IsActive = 1 AND col.IsActive = 1
    ORDER BY c.GridType, col.SortOrder;
END

Text
---------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------

CREATE   PROCEDURE dbo.APS_InboxConfig_Save
    @UseCaseID           int,
    @GridType            tinyint,
    @StatusProcedureName varchar(128) = NULL,
    @Columns             dbo.APS_InboxColumnType READONLY,
    @ActionBy            varchar(30),
   
 @Success             tinyint OUTPUT,
    @Message             varchar(200) OUTPUT
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;
    SET @Success = 0;

    IF NOT EXISTS (SELECT 1 FROM @Columns)
    BEGIN
        SET @Message = 'Select at least one 
column for this grid.';
        RETURN;
    END

    -- A status grid runs a procedure by name, so the name is checked against
    -- sys.procedures rather than trusted. A screen must not be able to nominate
    -- an object that does not exist, or one ou
tside the APS_ family.
    IF @GridType = 2
    BEGIN
        IF LEN(LTRIM(RTRIM(ISNULL(@StatusProcedureName, '')))) = 0
        BEGIN
            SET @Message = 'Enter the stored procedure name for the status grid.';
            RETURN;
        END

    
    IF LEFT(@StatusProcedureName, 6) <> 'APS_'
        BEGIN
            SET @Message = 'The status procedure name must start with APS_.';
            RETURN;
        END

        IF NOT EXISTS (SELECT 1 FROM sys.procedures WHERE name = @StatusProcedureNa
me)
        BEGIN
            SET @Message = 'That stored procedure does not exist in this database.';
            RETURN;
        END
    END

    DECLARE @configId int;

    BEGIN TRY
        BEGIN TRANSACTION;

        SELECT @configId = InboxConfigID

        FROM   dbo.APS_InboxConfig
        WHERE  UseCaseID = @UseCaseID AND GridType = @GridType;

        IF @configId IS NULL
        BEGIN
            INSERT INTO dbo.APS_InboxConfig (UseCaseID, GridType, StatusProcedureName, CreatedBy)
            VA
LUES (@UseCaseID, @GridType, @StatusProcedureName, @ActionBy);

            SET @configId = CAST(SCOPE_IDENTITY() AS int);
        END
        ELSE
        BEGIN
            UPDATE dbo.APS_InboxConfig
               SET StatusProcedureName = @StatusProced
ureName, IsActive = 1,
                   ModifiedBy = @ActionBy, ModifiedDate = GETDATE()
             WHERE InboxConfigID = @configId;
        END

        DELETE FROM dbo.APS_InboxColumn WHERE InboxConfigID = @configId;

        INSERT INTO dbo.APS_Inb
oxColumn (InboxConfigID, FieldCode, DisplayName, SortOrder, CreatedBy)
        SELECT @configId, FieldCode, DisplayName, SortOrder, @ActionBy
        FROM   @Columns;

        EXEC dbo.APS_ConfigHistory_Insert
             @UseCaseID = @UseCaseID,
       
      @ConfigItem = 'Inbox layout',
             @Detail = 'Grid type ',
             @ActionBy = @ActionBy;

        COMMIT TRANSACTION;
    END TRY
    BEGIN CATCH
        IF XACT_STATE() <> 0 ROLLBACK TRANSACTION;
        SET @Message = 'The inbox layo
ut could not be saved.';
        RETURN;
    END CATCH

    SET @Success = 1;
    SET @Message = 'Inbox layout saved.';
END


Text
---------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------

/* --- Inbox grid 2 fallback -------------------------------------------------
   Used when an Admin has not nominated a status procedure yet, so the second
   grid shows something real instead of an error. Its signature is the contract
   a custom proce
dure must match: @UseCaseID and @UserName. */
CREATE   PROCEDURE dbo.APS_InboxStatus_Default
    @UseCaseID int,
    @UserName  varchar(50) = NULL
AS
BEGIN
    SET NOCOUNT ON;
    SELECT InflowDataID,
           RowNumber,
           ReviewStatus     = Re
viewStatusCode,
           ProcessingStatus = ProcessingStatusCode,
           AssignedTo       = ISNULL(AssignedTo, 'Unassigned'),
           UpdatedOn        = ISNULL(ModifiedDate, CreatedDate)
    FROM   dbo.APS_InflowData
    WHERE  UseCaseID = @UseCa
seID
      AND  IsActive = 1
      AND (@UserName IS NULL OR AssignedTo IS NULL OR AssignedTo = @UserName)
    ORDER BY InflowDataID DESC;
END


Text
---------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------

CREATE   PROCEDURE dbo.APS_Inflow_Finalize
    @InflowID        int,
    @ValidRowCount   int,
    @InvalidRowCount int,
    @StatusCode      varchar(50),
    @ActionBy        varchar(30)
AS
BEGIN
    SET NOCOUNT ON;
    UPDATE dbo.APS_Inflow
       SET 
ValidRowCount = @ValidRowCount,
           InvalidRowCount = @InvalidRowCount,
           StatusCode = @StatusCode,
           ModifiedBy = @ActionBy,
           ModifiedDate = GETDATE()
     WHERE InflowID = @InflowID;
END


Text
---------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------

/* -----------------------------------------------------------------------------
   One batch, for the Inflow history drill-down.
   ----------------------------------------------------------------------------- */
CREATE   PROCEDURE dbo.APS_Inflow_GetByI
d
    @InflowID int
AS
BEGIN
    SET NOCOUNT ON;

    SELECT i.InflowID, i.UseCaseID, u.UseCaseCode, i.InflowConfigID, f.VersionNo,
           i.OriginalFileName, i.UploadedBy, i.UploadedOn,
           i.TotalRowCount, i.ValidRowCount, i.InvalidRowCount, 
i.StatusCode
    FROM   dbo.APS_Inflow i
    INNER  JOIN dbo.APS_UseCaseMaster u ON u.UseCaseID = i.UseCaseID
    INNER  JOIN dbo.APS_InflowConfig  f ON f.InflowConfigID = i.InflowConfigID
    WHERE  i.InflowID = @InflowID;

    /* Rejected rows only. A c
lean import returns nothing here, which is the
       point: the screen shows a problem list, not a data dump. */
    SELECT TOP (200) InflowDataID, RowNumber, ValidationMessage
    FROM   dbo.APS_InflowData
    WHERE  InflowID = @InflowID AND IsEligible 
= 0
    ORDER BY RowNumber;
END


Text
---------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------

CREATE   PROCEDURE dbo.APS_Inflow_GetHistory
    @UseCaseID int = NULL,
    @Top       int = 10
AS
BEGIN
    SET NOCOUNT ON;
    SELECT TOP (@Top)
           i.InflowID, i.UseCaseID, u.UseCaseCode, i.InflowConfigID, f.VersionNo,
           i.OriginalFile
Name, i.UploadedBy, i.UploadedOn,
           i.TotalRowCount, i.ValidRowCount, i.InvalidRowCount, i.StatusCode
    FROM   dbo.APS_Inflow i
    INNER  JOIN dbo.APS_UseCaseMaster u ON u.UseCaseID = i.UseCaseID
    INNER  JOIN dbo.APS_InflowConfig  f ON f.In
flowConfigID = i.InflowConfigID
    WHERE  (@UseCaseID IS NULL OR i.UseCaseID = @UseCaseID)
    ORDER BY i.InflowID DESC;
END


Text
---------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------

/* =============================================================================
   RUNTIME INFLOW
   ============================================================================= */

CREATE   PROCEDURE dbo.APS_Inflow_Insert
    @UseCaseID        int,
  
  @InflowConfigID   int,
    @OriginalFileName varchar(260),
    @StoredFileName   varchar(260) = NULL,
    @TotalRowCount    int,
    @UploadedBy       varchar(30),
    @InflowID         int OUTPUT,
    @Message          varchar(200) OUTPUT
AS
BEGIN
    
SET NOCOUNT ON;
    SET @InflowID = 0;

    IF NOT EXISTS (SELECT 1 FROM dbo.APS_InflowConfig
                   WHERE InflowConfigID = @InflowConfigID
                     AND UseCaseID = @UseCaseID
                     AND IsActiveTemplate = 1 AND IsAct
ive = 1)
    BEGIN
        SET @Message = 'The active template changed while the file was being read. Reload and try again.';
        RETURN;
    END

    INSERT INTO dbo.APS_Inflow
        (UseCaseID, InflowConfigID, OriginalFileName, StoredFileName,
   
      UploadedBy, TotalRowCount, StatusCode, CreatedBy)
    VALUES
        (@UseCaseID, @InflowConfigID, @OriginalFileName, @StoredFileName,
         @UploadedBy, @TotalRowCount, 'Importing', @UploadedBy);

    SET @InflowID = CAST(SCOPE_IDENTITY() AS int
);
    SET @Message = 'Batch created.';
END


Text
---------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------

/* -----------------------------------------------------------------------------
   Save a whole upload in one transaction.

   The batch header, its rows and the final counts are written together, so a
   failure part-way leaves nothing behind. Writing 
the header first and the rows
   afterwards is what produces a batch claiming fifty records with none stored.
   ----------------------------------------------------------------------------- */
CREATE   PROCEDURE dbo.APS_Inflow_Save
    @UseCaseID        
int,
    @InflowConfigID   int,
    @OriginalFileName varchar(260),
    @StoredFileName   varchar(260) = NULL,
    @TotalRowCount    int,
    @ValidRowCount    int,
    @InvalidRowCount  int,
    @Rows             dbo.APS_InflowDataType READONLY,
    @Upl
oadedBy       varchar(30),
    @InflowID         int OUTPUT,
    @Message          varchar(200) OUTPUT
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;
    SET @InflowID = 0;

    IF NOT EXISTS (SELECT 1 FROM @Rows)
    BEGIN
        SET @Message = 'Th
e workbook has no data rows.';
        RETURN;
    END

    /* The template may have been replaced while the file was being read. The
       batch must record the version it was actually validated against. */
    IF NOT EXISTS (SELECT 1 FROM dbo.APS_Inflo
wConfig
                   WHERE InflowConfigID = @InflowConfigID
                     AND UseCaseID = @UseCaseID
                     AND IsActiveTemplate = 1 AND IsActive = 1)
    BEGIN
        SET @Message = 'The active template changed while the file 
was being read. Reload the page and upload again.';
        RETURN;
    END

    BEGIN TRY
        BEGIN TRANSACTION;

        INSERT INTO dbo.APS_Inflow
            (UseCaseID, InflowConfigID, OriginalFileName, StoredFileName, UploadedBy,
             To
talRowCount, ValidRowCount, InvalidRowCount, StatusCode, CreatedBy)
        VALUES
            (@UseCaseID, @InflowConfigID, @OriginalFileName, @StoredFileName, @UploadedBy,
             @TotalRowCount, @ValidRowCount, @InvalidRowCount,
             CASE 
WHEN @InvalidRowCount = 0 THEN 'Imported' ELSE 'Imported with errors' END,
             @UploadedBy);

        SET @InflowID = CAST(SCOPE_IDENTITY() AS int);

        INSERT INTO dbo.APS_InflowData
            (InflowID, UseCaseID, RowNumber, RowDataJson,
 ValidationStatusCode,
             ValidationMessage, IsEligible, ProcessingStatusCode, ReviewStatusCode, CreatedBy)
        SELECT @InflowID, @UseCaseID, RowNumber, RowDataJson, ValidationStatusCode,
               ValidationMessage, IsEligible, 'Pendin
g',
               /* An invalid row is not put in front of a reviewer as if it were
                  ready. It lands in its own bucket instead. */
               CASE WHEN IsEligible = 1 THEN 'Pending Review' ELSE 'Validation Failed' END,
              
 @UploadedBy
        FROM   @Rows;

        COMMIT TRANSACTION;
    END TRY
    BEGIN CATCH
        IF XACT_STATE() <> 0 ROLLBACK TRANSACTION;
        SET @InflowID = 0;
        SET @Message = 'The upload could not be saved. Nothing was written.';
       
 RETURN;
    END CATCH

    SET @Message = CAST(@ValidRowCount AS varchar(10)) + ' row(s) imported'
                 + CASE WHEN @InvalidRowCount > 0
                        THEN ', ' + CAST(@InvalidRowCount AS varchar(10)) + ' rejected by validation.'
  
                      ELSE '.' END;
END


Text
---------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------

/* --- The columns of one specific version, for the version comparison -------- */
CREATE   PROCEDURE dbo.APS_InflowColumnConfig_GetByConfig
    @InflowConfigID int
AS
BEGIN
    SET NOCOUNT ON;
    SELECT ColumnConfigID, InflowConfigID, ExcelColumnName, 
FieldCode, DataType,
           ColumnLength, IsRequired, ShowInInbox, ShowInDetail, SortOrder
    FROM   dbo.APS_InflowColumnConfig
    WHERE  InflowConfigID = @InflowConfigID AND IsActive = 1
    ORDER BY SortOrder;
END


Text
---------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------

/* =============================================================================
   INFLOW TEMPLATE CONFIGURATION
   ============================================================================= */

/* --- The active template for a use case, plus its col
umns ------------------
   Returns two result sets: the config row, then its columns. */
CREATE   PROCEDURE dbo.APS_InflowConfig_GetActive
    @UseCaseID int
AS
BEGIN
    SET NOCOUNT ON;

    DECLARE @configId int =
        (SELECT TOP 1 InflowConfigID
  
       FROM   dbo.APS_InflowConfig
         WHERE  UseCaseID = @UseCaseID AND IsActiveTemplate = 1 AND IsActive = 1
         ORDER BY VersionNo DESC);

    SELECT f.InflowConfigID, f.UseCaseID, f.VersionNo, f.InflowSourceType, f.FileType,
           f.Tem
plateName, f.TemplateFileName, f.TemplateHash, f.ColumnCount,
           f.IsAcknowledged, f.IsActiveTemplate, f.ActivatedOn, f.CreatedBy, f.CreatedDate,
           u.UseCaseCode, u.UseCaseName, u.ClientID, u.ProcessID
    FROM   dbo.APS_InflowConfig f
  
  INNER  JOIN dbo.APS_UseCaseMaster u ON u.UseCaseID = f.UseCaseID
    WHERE  f.InflowConfigID = @configId;

    SELECT ColumnConfigID, InflowConfigID, ExcelColumnName, FieldCode, DataType,
           ColumnLength, IsRequired, ShowInInbox, ShowInDetail, S
ortOrder
    FROM   dbo.APS_InflowColumnConfig
    WHERE  InflowConfigID = @configId AND IsActive = 1
    ORDER BY SortOrder;
END


Text
---------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------

/* --- Every version a use case has had, newest first ------------------------ */
CREATE   PROCEDURE dbo.APS_InflowConfig_GetVersions
    @UseCaseID int
AS
BEGIN
    SET NOCOUNT ON;
    SELECT InflowConfigID, VersionNo, TemplateName, TemplateFileName, Fi
leType,
           ColumnCount, IsActiveTemplate, ActivatedOn, DeactivatedOn,
           CreatedBy, CreatedDate
    FROM   dbo.APS_InflowConfig
    WHERE  UseCaseID = @UseCaseID AND IsActive = 1
    ORDER BY VersionNo DESC;
END


Text
---------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------

/* --- Save a template version ----------------------------------------------
   One active template per use case. Saving deactivates the current one and
   inserts the next version number in a single transaction, so there is never a
   moment where a us
e case has two active templates or none.

   @ReplaceExisting guards the confirmation step: the first save of a use case
   that already has a template returns AlreadyExists = 1 and writes nothing, so
   the screen can ask before overwriting. */
CREATE   
PROCEDURE dbo.APS_InflowConfig_Save
    @UseCaseID        int,
    @InflowSourceType tinyint,
    @FileType         varchar(20),
    @TemplateName     varchar(200),
    @TemplateFileName varchar(260),
    @TemplateHash     char(64),
    @IsAcknowledged   
tinyint,
    @ReplaceExisting  tinyint,
    @Columns          dbo.APS_InflowColumnType READONLY,
    @ActionBy         varchar(30),
    @NewInflowConfigID int OUTPUT,
    @AlreadyExists    tinyint OUTPUT,
    @Message          varchar(200) OUTPUT
AS
BEGIN

    SET NOCOUNT ON;
    SET XACT_ABORT ON;

    SET @NewInflowConfigID = 0;
    SET @AlreadyExists = 0;

    IF @IsAcknowledged = 0
    BEGIN
        SET @Message = 'Tick the confirmation before saving the configuration.';
        RETURN;
    END

    IF
 NOT EXISTS (SELECT 1 FROM @Columns)
    BEGIN
        SET @Message = 'Apply the template columns before saving.';
        RETURN;
    END

    IF NOT EXISTS (SELECT 1 FROM dbo.APS_UseCaseMaster WHERE UseCaseID = @UseCaseID AND IsActive = 1)
    BEGIN
   
     SET @Message = 'Select an active client and process.';
        RETURN;
    END

    DECLARE @currentId int, @currentVersion int = 0, @currentHash char(64);

    SELECT TOP 1 @currentId = InflowConfigID, @currentVersion = VersionNo, @currentHash = Tem
plateHash
    FROM   dbo.APS_InflowConfig
    WHERE  UseCaseID = @UseCaseID AND IsActiveTemplate = 1 AND IsActive = 1
    ORDER BY VersionNo DESC;

    -- An identical template is not a new version.
    IF @currentId IS NOT NULL AND @currentHash = @Templa
teHash
    BEGIN
        SET @NewInflowConfigID = @currentId;
        SET @Message = 'This template is identical to the active version. Nothing was changed.';
        RETURN;
    END

    IF @currentId IS NOT NULL AND @ReplaceExisting = 0
    BEGIN
      
  SET @AlreadyExists = 1;
        SET @Message = 'An active template already exists for this use case. Replace it?';
        RETURN;
    END

    DECLARE @nextVersion int =
        ISNULL((SELECT MAX(VersionNo) FROM dbo.APS_InflowConfig WHERE UseCaseID = 
@UseCaseID), 0) + 1;

    BEGIN TRY
        BEGIN TRANSACTION;

        IF @currentId IS NOT NULL
            UPDATE dbo.APS_InflowConfig
               SET IsActiveTemplate = 0, DeactivatedOn = GETDATE(),
                   ModifiedBy = @ActionBy, Modifi
edDate = GETDATE()
             WHERE InflowConfigID = @currentId;

        INSERT INTO dbo.APS_InflowConfig
            (UseCaseID, VersionNo, InflowSourceType, FileType, TemplateName, TemplateFileName,
             TemplateHash, ColumnCount, IsAcknowled
ged, IsActiveTemplate, ActivatedOn, CreatedBy)
        VALUES
            (@UseCaseID, @nextVersion, @InflowSourceType, @FileType, @TemplateName, @TemplateFileName,
             @TemplateHash, (SELECT COUNT(*) FROM @Columns), @IsAcknowledged, 1, GETDATE()
, @ActionBy);

        SET @NewInflowConfigID = CAST(SCOPE_IDENTITY() AS int);

        INSERT INTO dbo.APS_InflowColumnConfig
            (InflowConfigID, UseCaseID, ExcelColumnName, FieldCode, DataType, ColumnLength,
             IsRequired, ShowInInbox
, ShowInDetail, SortOrder, CreatedBy)
        SELECT @NewInflowConfigID, @UseCaseID, ExcelColumnName, FieldCode, DataType, ColumnLength,
               IsRequired, ShowInInbox, ShowInDetail, SortOrder, @ActionBy
        FROM   @Columns;

        EXEC dbo.
APS_ConfigHistory_Insert
             @UseCaseID = @UseCaseID,
             @ConfigItem = 'Inflow template',
             @Detail = @TemplateFileName,
             @ActionBy = @ActionBy;

        COMMIT TRANSACTION;
    END TRY
    BEGIN CATCH
        IF 
XACT_STATE() <> 0 ROLLBACK TRANSACTION;
        SET @NewInflowConfigID = 0;
        SET @Message = 'The template could not be saved.';
        RETURN;
    END CATCH

    SET @Message = 'Template version ' + CAST(@nextVersion AS varchar(10)) + ' is now act
ive.';
END


Text
---------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------

/* -----------------------------------------------------------------------------
   5. Archive the live rows before a rebuild

   Called by the controller immediately before APS_InflowData_Rebuild. Built
   dynamically because APS_InflowData has no fixed
 column list - its shape comes
   from whichever template is active. FOR JSON AUTO turns whatever those columns
   happen to be into one payload per row.
   ----------------------------------------------------------------------------- */
CREATE   PROCEDUR
E dbo.APS_InflowData_Archive
    @cid       smallint,
    @actionby  varchar(20),
    @rowcount  int OUTPUT
AS
BEGIN
    SET NOCOUNT ON;
    SET @rowcount = 0;

    IF OBJECT_ID('dbo.APS_InflowData', 'U') IS NULL RETURN;

    IF NOT EXISTS (SELECT 1 FROM 
dbo.APS_InflowData) RETURN;

    DECLARE @sql nvarchar(MAX) = N'
        INSERT INTO dbo.APS_InflowDataArchive (ifdid, ifid, cid, rowjson, archivedby)
        SELECT d.ifdid, d.ifid, @cid,
               (SELECT x.* FROM dbo.APS_InflowData x
             
   WHERE x.ifdid = d.ifdid FOR JSON AUTO, WITHOUT_ARRAY_WRAPPER),
               @actionby
        FROM   dbo.APS_InflowData d;';

    BEGIN TRY
        EXEC sp_executesql @sql,
             N'@cid smallint, @actionby varchar(20)',
             @cid = @ci
d, @actionby = @actionby;

        SET @rowcount = @@ROWCOUNT;
    END TRY
    BEGIN CATCH
        /* Archiving must never block a template change, but the caller needs to
           know it did not happen. Zero means nothing was archived. */
        SET 
@rowcount = -1;
    END CATCH
END


Text
---------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------

/* --- Inbox grid 1: the uploaded rows ---------------------------------------
   RowDataJson is returned whole. The controller reads the configured fields out
   of it, which keeps this procedure independent of any template. */
CREATE   PROCEDURE dbo.AP
S_InflowData_Get
    @UseCaseID    int,
    @ReviewStatus varchar(50) = NULL,
    @Top          int = 500
AS
BEGIN
    SET NOCOUNT ON;
    SELECT TOP (@Top)
           d.InflowDataID, d.InflowID, d.UseCaseID, d.RowNumber, d.RowDataJson,
           d.Valid
ationStatusCode, d.ValidationMessage, d.IsEligible,
           d.ProcessingStatusCode, d.ReviewStatusCode, d.AssignedTo, d.CreatedDate
    FROM   dbo.APS_InflowData d
    WHERE  d.UseCaseID = @UseCaseID
      AND  d.IsActive = 1
      AND (@ReviewStatus I
S NULL OR d.ReviewStatusCode = @ReviewStatus)
    ORDER BY d.InflowDataID DESC;
END


Text
---------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------

CREATE   PROCEDURE dbo.APS_InflowData_GetById
    @InflowDataID int
AS
BEGIN
    SET NOCOUNT ON;
    SELECT d.InflowDataID, d.InflowID, d.UseCaseID, u.UseCaseCode, d.RowNumber,
           d.RowDataJson, d.ValidationStatusCode, d.ValidationMessage,
      
     d.IsEligible, d.ProcessingStatusCode, d.ReviewStatusCode, d.AssignedTo,
           d.CreatedBy, d.CreatedDate
    FROM   dbo.APS_InflowData d
    INNER  JOIN dbo.APS_UseCaseMaster u ON u.UseCaseID = d.UseCaseID
    WHERE  d.InflowDataID = @InflowData
ID;
END


Text
---------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------

CREATE   PROCEDURE dbo.APS_InflowData_GetKpi
    @UseCaseID int
AS
BEGIN
    SET NOCOUNT ON;
    SELECT ReviewStatusCode, RecordCount = COUNT(*)
    FROM   dbo.APS_InflowData
    WHERE  UseCaseID = @UseCaseID AND IsActive = 1
    GROUP BY ReviewStatusCod
e;
END


Text
---------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------

/* -----------------------------------------------------------------------------
   Record a review decision.

   Only a row still awaiting review can be decided, so two reviewers opening the
   same record cannot both record an outcome.
   -------------
---------------------------------------------------------------- */
CREATE   PROCEDURE dbo.APS_InflowData_SaveReview
    @InflowDataID    int,
    @ReviewStatusCode varchar(50),
    @ReviewComment   varchar(2000) = NULL,
    @ActionBy        varchar(30),

    @Success         tinyint OUTPUT,
    @Message         varchar(200) OUTPUT
AS
BEGIN
    SET NOCOUNT ON;
    SET @Success = 0;

    IF @ReviewStatusCode NOT IN ('Approved', 'Rejected', 'Needs Clarification')
    BEGIN
        SET @Message = 'Unknown rev
iew decision.';
        RETURN;
    END

    IF @ReviewStatusCode IN ('Rejected', 'Needs Clarification')
       AND LEN(LTRIM(RTRIM(ISNULL(@ReviewComment, '')))) = 0
    BEGIN
        SET @Message = 'Add a comment explaining the decision.';
        RETURN
;
    END

    DECLARE @current varchar(50);
    SELECT @current = ReviewStatusCode FROM dbo.APS_InflowData WHERE InflowDataID = @InflowDataID;

    IF @current IS NULL
    BEGIN
        SET @Message = 'That record no longer exists.';
        RETURN;
    
END

    IF @current <> 'Pending Review'
    BEGIN
        SET @Message = 'That record was already decided as ' + @current + '.';
        RETURN;
    END

    UPDATE dbo.APS_InflowData
       SET ReviewStatusCode = @ReviewStatusCode,
           ReviewComm
ent    = @ReviewComment,
           ReviewedBy       = @ActionBy,
           ReviewedOn       = GETDATE(),
           AssignedTo       = @ActionBy,
           ProcessingStatusCode = CASE WHEN @ReviewStatusCode = 'Approved' THEN 'Ready' ELSE 'Held' END,
  
         ModifiedBy       = @ActionBy,
           ModifiedDate     = GETDATE()
     WHERE InflowDataID = @InflowDataID
       AND ReviewStatusCode = 'Pending Review';

    IF @@ROWCOUNT = 0
    BEGIN
        SET @Message = 'Another reviewer decided that r
ecord first. Refresh the Inbox.';
        RETURN;
    END

    SET @Success = 1;
    SET @Message = 'Recorded as ' + @ReviewStatusCode + '.';
END


Text
---------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------

CREATE   PROCEDURE dbo.APS_InflowData_UpdateStatus
    @ifdid        int,
    @reviewstatus varchar(50)
AS
BEGIN
    SET NOCOUNT ON;
    UPDATE dbo.APS_InflowData SET reviewstatus = @reviewstatus WHERE ifdid = @ifdid;
END


Text
---------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------

/* --- read archived rows back ------------------------------------------------ */
CREATE   PROCEDURE dbo.APS_InflowDataArchive_Get
    @cid  smallint = NULL,
    @ifid int = NULL,
    @top  int = 200
AS
BEGIN
    SET NOCOUNT ON;
    SELECT TOP (@top) a.
archiveid, a.ifdid, a.ifid, a.cid, c.versionno,
           a.rowjson, a.archivedon, a.archivedby
    FROM   dbo.APS_InflowDataArchive a
    LEFT   JOIN dbo.APS_Configuration c ON c.cid = a.cid
    WHERE  (@cid IS NULL OR a.cid = @cid)
      AND  (@ifid IS
 NULL OR a.ifid = @ifid)
    ORDER BY a.archiveid DESC;
END


Text
---------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------
CREATE PROCEDURE APS_InflowDataReport_Get
(
    @Company     NVARCHAR(100) = NULL,
    @Date        DATE = NULL,
    @ActionDate  DATE = NULL,
    @Status      NVARCHAR(50) = NULL
)
AS
BEGIN
    SET NOCOUNT ON;

    SELECT *
    FROM APS_InflowData
    WHERE
        (@Company IS NULL OR Company = @Company)
        AND (@Date IS NULL OR [Date] = @Date)
        AND (@ActionDate IS NULL OR Action_date = @ActionDate)
        AND (@Status IS NULL OR Status = @Status);
END;

Text
---------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------
CREATE PROCEDURE APS_ManageCategory_Delete
(
    @CategoryId INT
)
AS
BEGIN
    SET NOCOUNT ON;

    UPDATE APS_ManageCategory SET Active = 0
    WHERE CategoryId = @CategoryId;

    SELECT @@ROWCOUNT AS RowsAffected;
END;

Text
---------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------
CREATE PROCEDURE APS_ManageCategory_Get
AS
BEGIN
    SET NOCOUNT ON;

    SELECT
        CategoryId,
        CategoryName,
        Active,
        CreatedBy,
        CreatedOn
    FROM APS_ManageCategory
    ORDER BY CategoryName;
END;

Text
---------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------
CREATE PROCEDURE APS_ManageCategory_Insert
(
    @CategoryName NVARCHAR(255),
    @Active BIT = 1,
    @CreatedBy NVARCHAR(100)
)
AS
BEGIN
    SET NOCOUNT ON;

    INSERT INTO APS_ManageCategory
    (
        CategoryName,
        Active,
        CreatedBy,
        CreatedOn
    )
    VALUES
    (
        @CategoryName,
        @Active,
        @CreatedBy,
        GETDATE()
    );

    SELECT SCOPE_IDENTITY() AS CategoryId;
END;

Text
---------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------
CREATE PROCEDURE dbo.APS_OutputType_GetAll
AS
BEGIN
    SET NOCOUNT ON;

    SELECT *
    FROM dbo.APS_OutputType
    ORDER BY OutputTypeID;
END


Text
---------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------

CREATE   PROCEDURE dbo.APS_ProcessMaster_Get
    @ActiveOnly tinyint = 1
AS
BEGIN
    SET NOCOUNT ON;
    SELECT ProcessID, ProcessCode, ProcessName, IsActive
    FROM   dbo.APS_ProcessMaster
    WHERE  (@ActiveOnly = 0 OR IsActive = 1)
    ORDER BY Proc
essName;
END


Text
---------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------
CREATE PROCEDURE APS_Report_Configuration_Get  
@reportId INT  
AS  
BEGIN  
   SELECT * FROM APS_ReportMaster WHERE ReportId = @reportId  
   SELECT * FROM APS_ReportParameter WHERE ReportId = @reportId  
   SELECT * FROM ASP_ReportColumns WHERE ReportId = @reportId  
END

Text
---------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------
CREATE PROCEDURE dbo.APS_ReportConfiguration_Get
    @ReportId INT
AS
BEGIN
    SET NOCOUNT ON;

    -- Report Master Details
    SELECT *
    FROM APS_ReportMaster
    WHERE ReportId = @ReportId;

    -- Report Parameter Details
    SELECT *
    FROM APS_ReportParameter
    WHERE ReportId = @ReportId;
END;

Text
---------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------
CREATE PROCEDURE APS_ReportMaster_Get
--@reportId INT,
--@pageNumber INT,
--@pageSize INT
AS
BEGIN
   SELECT * FROM APS_ReportMaster --WHERE ReportId = @reportId
END

Text
---------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------
CREATE PROCEDURE dbo.APS_ReportMaster_Insert
(
      @ReportName           VARCHAR(200)
    , @ReportCode           VARCHAR(200)
    , @Description          VARCHAR(MAX) = NULL
    , @ProcedureSchema      VARCHAR(200)
    , @ProcedureName        VARCHAR(100)
    , @ExecutionMode        VARCHAR(100) = NULL
    , @DefaultPageSize      INT = 10
    , @MaxInteractiveRows   INT = 5000
    , @CommandTimeout       INT = 120
    , @IsActive             BIT = 1

    , @ReportId             SMALLINT OUTPUT
    , @Message              NVARCHAR(400) OUTPUT
)
AS
BEGIN
    SET NOCOUNT ON;

    BEGIN TRY

        -- Duplicate Report Code Validation
        IF EXISTS
        (
            SELECT 1
            FROM APS_ReportMaster
            WHERE ReportCode = @ReportCode
        )
        BEGIN
            SET @ReportId = 0;
            SET @Message = 'Report Code already exists.';
            RETURN;
        END

        INSERT INTO APS_ReportMaster
        (
              ReportName
            , ReportCode
            , Description
            , ProcedureSchema
            , ProcedureName
            , ExecutionMode
            , DefaultPageSize
            , MaxInteractiveRows
            , CommandTimeout
            , IsActive
        )
        VALUES
        (
              @ReportName
            , @ReportCode
            , @Description
            , @ProcedureSchema
            , @ProcedureName
            , @ExecutionMode
            , @DefaultPageSize
            , @MaxInteractiveRows
            , @CommandTimeout
            , @IsActive
        );

        SET @ReportId = CAST(SCOPE_IDENTITY() AS SMALLINT);
        SET @Message = 'Report created successfully.';

    END TRY
    BEGIN CATCH

        SET @ReportId = 0;
        SET @Message = ERROR_MESSAGE();

    END CATCH
END

Text
---------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------

CREATE PROCEDURE APS_ReportParameter_Get
AS
BEGIN
   SELECT * FROM APS_ReportParameter
END

Text
---------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------
CREATE PROCEDURE dbo.APS_ReportParameter_Insert  
(  
      @ReportId INT  
    , @ParameterName VARCHAR(100)  
    , @DisplayName VARCHAR(50)  
    , @DataType VARCHAR(50)  
    , @ControlType VARCHAR(50)  
    , @DefaultValue VARCHAR(50)  
    , @IsRequired BIT  
)  
AS  
BEGIN  
    SET NOCOUNT ON;  
  
    INSERT INTO dbo.APS_ReportParameter  
    (  
          ReportId  
        , ParameterName  
        , DisplayName  
        , DataType  
        , ControlType  
        , DefaultValue  
        , IsRequired  
    )  
    VALUES  
    (  
          @ReportId  
        , @ParameterName  
        , @DisplayName  
        , @DataType  
        , @ControlType  
        , @DefaultValue  
        , @IsRequired  
    );  
  
    SELECT  
          CAST(SCOPE_IDENTITY() AS INT) AS ParameterId  
        , 1 AS IsSuccess  
        , 'Parameter saved successfully.' AS Message;  
END 

Text
---------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------

CREATE   PROCEDURE dbo.APS_Review_GetByRecord
    @ifdid int
AS
BEGIN
    SET NOCOUNT ON;
    SELECT rvid, ifdid, decision, comments, reviewedby, reviewedon
    FROM   dbo.APS_Review
    WHERE  ifdid = @ifdid
    ORDER BY rvid DESC;
END


Text
---------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------

/* =============================================================================
   9. HUMAN REVIEW
   ============================================================================= */
CREATE   PROCEDURE dbo.APS_Review_Insert
    @ifdid      int,
    @dec
ision   varchar(50),
    @comments   nvarchar(MAX) = NULL,
    @reviewedby varchar(20),
    @success    bit OUTPUT,
    @message    nvarchar(200) OUTPUT
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;   -- roll back automatically if any statement fail
s
    SET @success = 0;

    IF NOT EXISTS (SELECT 1 FROM dbo.APS_InflowData WHERE ifdid = @ifdid)
    BEGIN
        SET @message = 'Record not found.';
        RETURN;
    END

    BEGIN TRANSACTION;

    INSERT INTO dbo.APS_Review (ifdid, decision, comm
ents, reviewedby, reviewedon)
    VALUES (@ifdid, @decision, @comments, @reviewedby, GETDATE());

    UPDATE dbo.APS_InflowData SET reviewstatus = @decision WHERE ifdid = @ifdid;

    COMMIT TRANSACTION;
    SET @success = 1;
    SET @message = 'Review re
corded as ' + @decision + '.';
END


Text
---------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------
CREATE PROCEDURE [dbo].[APS_ReviewStatus]
(
    @Status      NVARCHAR(50) = NULL
)
AS
BEGIN
    SET NOCOUNT ON;

    SELECT *
    FROM APS_InflowData
    WHERE
        (@Status IS NULL OR reviewstatus = @Status);
END;

Text
---------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------

CREATE   PROCEDURE dbo.APS_RoleMaster_Get
AS
BEGIN
    SET NOCOUNT ON;
    SELECT RoleID, RoleName, IsActive
    FROM   dbo.APS_RoleMaster
    WHERE  IsActive = 1
    ORDER BY RoleID;
END


Text
---------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------
CREATE PROCEDURE dbo.APS_SourceRegistry_Delete
(
    @SourceId INT
)
AS
BEGIN
    SET NOCOUNT ON;

    BEGIN TRY
        -- Check whether record exists
        IF NOT EXISTS (
            SELECT 1
            FROM dbo.APS_SourceRegistry
            WHERE SourceId = @SourceId
        )
        BEGIN
            RAISERROR('Source Registry record not found.', 16, 1);
            RETURN;
        END

        -- Delete record
        UPDATE dbo.APS_SourceRegistry SET Status = 0
        WHERE SourceId = @SourceId;

        -- Success message
        SELECT
            1 AS Success,
            'Record deleted successfully.' AS Message;
    END TRY
    BEGIN CATCH
        SELECT
            0 AS Success,
            ERROR_MESSAGE() AS Message;
    END CATCH
END;

Text
---------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------
CREATE PROCEDURE APS_SourceRegistry_Get     
AS      
BEGIN      
    SET NOCOUNT ON;      
      
    SELECT 
            SourceId, 
            SourceName , 
            SourceCode , 
            SourceType, 
            AccessInterface, 
            OwnerTeam , 
            CASE 
            WHEN Status = 1 THEN 'TRUE' 
            WHEN Status = 0 THEN 'FALSE' END AS Status,
            DataClassification,
            AuthenticationConnection,
            Description,
            DeveloperNotesConfiguration
    FROM dbo.APS_SourceRegistry WITH(NOLOCK) WHERE Status = 1
    ORDER BY SourceID;
END; 

Text
---------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------
CREATE PROCEDURE APS_SourceRegistry_GetByid   
    @SourceId INT = NULL    
AS    
BEGIN    
    SET NOCOUNT ON;    
    
    SELECT *    
    FROM dbo.APS_SourceRegistry    
    WHERE (SourceId IS NULL OR SourceId = @SourceId);    
END; 

Text
---------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------
CREATE PROCEDURE APS_SourceRegistry_Save  
(  
    @SourceId                      INT = NULL,  
    @sourcename                    VARCHAR(255),  
    @sourcecode                    VARCHAR(100),  
    @sourcetype                    VARCHAR(100) = NULL,  
    @accessinterface               VARCHAR(255) = NULL,  
    @ownerteam                     VARCHAR(255) = NULL,  
    @status                        VARCHAR(50) = NULL,  
    @dataclassification            VARCHAR(100) = NULL,  
    @authenticationConnection      VARCHAR(255) = NULL, 
    @description                   VARCHAR(1000) = NULL,
    @developerNotesConfiguration   VARCHAR(2000) = NULL  
)  
AS  
BEGIN  
    SET NOCOUNT ON;  
  
    -- INSERT  
    IF ISNULL(@SourceId, 0) = 0  
    BEGIN  
        IF EXISTS  
        (  
            SELECT 1  
            FROM APS_SourceRegistry  
            WHERE SourceCode = @SourceCode  
        )  
        BEGIN  
            RAISERROR('Source Code already exists.', 16, 1);  
            RETURN;  
        END  
  
        INSERT INTO APS_SourceRegistry  
        (  
            SourceName,  
            SourceCode,  
            SourceType,  
            AccessInterface,  
            OwnerTeam,  
            Status,  
            DataClassification,  
            AuthenticationConnection,  
            Description,  
            DeveloperNotesConfiguration  
        )  
        VALUES  
        (  
            @SourceName,  
            @SourceCode,  
            @SourceType,  
            @AccessInterface,  
            @OwnerTeam,  
            @Status,  
            @DataClassification,  
            @AuthenticationConnection,  
            @Description,  
            @DeveloperNotesConfiguration  
        );  
  
        SELECT SCOPE_IDENTITY() AS SourceId,  
               'Inserted Successfully' AS Message;  
    END  
    ELSE  
    BEGIN  
        -- UPDATE  
  
        IF EXISTS  
        (  
            SELECT 1  
            FROM APS_SourceRegistry  
            WHERE SourceCode = @SourceCode  
              AND SourceId <> @SourceId  
        )  
        BEGIN  
            RAISERROR('Source Code already exists.', 16, 1);  
            RETURN;  
        END  
  
        UPDATE APS_SourceRegistry  
        SET  
            SourceName = @SourceName,  
            SourceCode = @SourceCode,  
            SourceType = @SourceType,  
            AccessInterface = @AccessInterface,  
            OwnerTeam = @OwnerTeam,  
            Status = @Status,  
            DataClassification = @DataClassification,  
            AuthenticationConnection = @AuthenticationConnection,  
            Description = @Description,  
            DeveloperNotesConfiguration = @DeveloperNotesConfiguration  
        WHERE SourceId = @SourceId;  
  
        SELECT @SourceId AS SourceId,  
               'Updated Successfully' AS Message;  
    END  
END; 

Text
---------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------
CREATE PROCEDURE dbo.APS_SourceType_GetAll
AS
BEGIN
    SET NOCOUNT ON;

    SELECT
        SourceTypeID,
        SourceTypeName,
        Description,
        IsActive,
        CreatedDate
    FROM dbo.APS_SourceType
    ORDER BY SourceTypeName;
END


Text
---------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------

/* --- The CAA-SET dropdown -------------------------------------------------
   @UserName restricts the list to the use cases a reviewer is mapped to.
   An Admin passes NULL and sees everything. */
CREATE   PROCEDURE dbo.APS_UseCaseMaster_Get
    @Clie
ntID  int = NULL,
    @ProcessID int = NULL,
    @UserName  varchar(50) = NULL
AS
BEGIN
    SET NOCOUNT ON;

    SELECT u.UseCaseID, u.UseCaseCode, u.UseCaseName,
           u.ClientID, c.ClientCode, c.ClientName,
           u.ProcessID, p.ProcessCode, p.
ProcessName,
           HasActiveTemplate = CASE WHEN EXISTS (
                SELECT 1 FROM dbo.APS_InflowConfig f
                WHERE f.UseCaseID = u.UseCaseID AND f.IsActiveTemplate = 1 AND f.IsActive = 1)
                THEN 1 ELSE 0 END
    FROM  
 dbo.APS_UseCaseMaster u
    INNER  JOIN dbo.APS_ClientMaster  c ON c.ClientID  = u.ClientID
    INNER  JOIN dbo.APS_ProcessMaster p ON p.ProcessID = u.ProcessID
    WHERE  u.IsActive = 1
      AND (@ClientID  IS NULL OR u.ClientID  = @ClientID)
      AND
 (@ProcessID IS NULL OR u.ProcessID = @ProcessID)
      AND (@UserName  IS NULL OR EXISTS (
                SELECT 1
                FROM   dbo.APS_UserUseCaseMapping m
                INNER  JOIN dbo.APS_UserMaster us ON us.UserID = m.UserID
            
    WHERE  m.UseCaseID = u.UseCaseID
                  AND  m.IsActive = 1
                  AND  us.UserName = @UserName))
    ORDER BY c.ClientCode, p.ProcessCode;
END


Text
---------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------

CREATE   PROCEDURE dbo.APS_UseCaseMaster_Save
    @UseCaseID  int,
    @ClientID   int,
    @ProcessID  int,
    @ActionBy   varchar(30),
    @NewUseCaseID int OUTPUT,
    @Message    varchar(200) OUTPUT
AS
BEGIN
    SET NOCOUNT ON;
    SET @NewUseCaseID
 = 0;

    DECLARE @code varchar(50), @name varchar(200);

    SELECT @code = c.ClientCode + '-' + p.ProcessCode,
           @name = c.ClientName + ' - ' + p.ProcessName
    FROM   dbo.APS_ClientMaster c
    CROSS  JOIN dbo.APS_ProcessMaster p
    WHERE  
c.ClientID = @ClientID AND p.ProcessID = @ProcessID;

    IF @code IS NULL
    BEGIN
        SET @Message = 'Select a valid client and process.';
        RETURN;
    END

    IF EXISTS (SELECT 1 FROM dbo.APS_UseCaseMaster
               WHERE UseCaseCode 
= @code AND UseCaseID <> @UseCaseID)
    BEGIN
        SET @Message = 'That client and process combination already exists.';
        RETURN;
    END

    IF @UseCaseID = 0
    BEGIN
        INSERT INTO dbo.APS_UseCaseMaster (ClientID, ProcessID, UseCaseCo
de, UseCaseName, CreatedBy)
        VALUES (@ClientID, @ProcessID, @code, @name, @ActionBy);

        SET @NewUseCaseID = CAST(SCOPE_IDENTITY() AS int);
        SET @Message = 'Use case created.';
        RETURN;
    END

    UPDATE dbo.APS_UseCaseMaster

       SET ClientID = @ClientID, ProcessID = @ProcessID,
           UseCaseCode = @code, UseCaseName = @name,
           ModifiedBy = @ActionBy, ModifiedDate = GETDATE()
     WHERE UseCaseID = @UseCaseID;

    SET @NewUseCaseID = @UseCaseID;
    SET @Mess
age = 'Use case updated.';
END


Text
---------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------

CREATE   PROCEDURE dbo.APS_UserMaster_GetByUserName
    @UserName varchar(50)
AS
BEGIN
    SET NOCOUNT ON;
    SELECT u.UserID, u.UserName, u.FirstName, u.LastName, u.EmailID,
           u.RoleID, r.RoleName, u.IsActive
    FROM   dbo.APS_UserMaster u
  
  INNER  JOIN dbo.APS_RoleMaster r ON r.RoleID = u.RoleID
    WHERE  u.UserName = @UserName AND u.IsActive = 1;
END


Text
---------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------

/* Used by the controller to refuse a use case the caller is not mapped to,
   whatever id arrived on the request. */
CREATE   PROCEDURE dbo.APS_UserUseCaseMapping_Check
    @username  varchar(30),
    @usecaseid int,
    @isallowed tinyint OUTPUT
AS
BEG
IN
    SET NOCOUNT ON;

    SET @isallowed = CASE WHEN EXISTS (
        SELECT 1 FROM dbo.APS_UserUseCaseMapping
        WHERE UserName = @username AND UseCaseID = @usecaseid AND IsActive = 1)
        THEN 1 ELSE 0 END;
END


Text
---------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------

CREATE PROCEDURE dbo.API_Config_Application_GetList
    @CustomerId INT = NULL
AS
BEGIN
    SET NOCOUNT ON;
    SELECT ApplicationId, CustomerId, ApplicationCode, ApplicationName
    FROM dbo.API_Config_Application
    WHERE Enabled = 1
      AND (@Custo
merId IS NULL OR CustomerId = @CustomerId)
    ORDER BY ApplicationName;
END


Text
---------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------

CREATE PROCEDURE dbo.API_Config_Audit_GetRecent
    @Top INT = 100
AS
BEGIN
    SET NOCOUNT ON;
    SELECT TOP (@Top)
           AuditEventId, EventType, EntityType, EntityId, ActorName, DetailsJson, CreatedOn
    FROM dbo.API_Config_AuditEvent
    ORDER
 BY AuditEventId DESC;
END


Text
---------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------
/* ============================================================================
   Agentic PITSTOP - API Configurator
   Script 02 : Stored procedures (all prefixed API_Config_)
   Requires SQL Server 2016 or later (OPENJSON / FOR JSON / STRING_SPLIT).
  
 ============================================================================ */


/* ----------------------------------------------------------------------------
   Drop existing procedures so the script is re-runnable.
   -------------------------------
--------------------------------------------- */
--DECLARE @sql NVARCHAR(MAX) = N'';
--SELECT @sql = @sql + N'DROP PROCEDURE dbo.' + QUOTENAME(name) + N';' + CHAR(10)
--FROM sys.procedures
--WHERE name LIKE N'API[_]Config[_]%';
--IF LEN(@sql) > 0 EXEC sp_
executesql @sql;
--GO

/* ============================================================================
   AUDIT
   ============================================================================ */
CREATE PROCEDURE dbo.API_Config_Audit_Insert
    @EventType 
  NVARCHAR(80),
    @EntityType  NVARCHAR(60)     = NULL,
    @EntityId    INT = NULL,
    @ActorName   NVARCHAR(128)    = NULL,
    @DetailsJson NVARCHAR(MAX)    = NULL
AS
BEGIN
    SET NOCOUNT ON;
    INSERT INTO dbo.API_Config_AuditEvent (EventType, En
tityType, EntityId, ActorName, DetailsJson)
    VALUES (@EventType, @EntityType, @EntityId, @ActorName, @DetailsJson);
END


Text
---------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------

CREATE PROCEDURE dbo.API_Config_CheckRun_GetLatest
    @ConnectorVersionId INT,
    @RunType            NVARCHAR(30) = NULL
AS
BEGIN
    SET NOCOUNT ON;
    SELECT TOP (1) CheckRunId, RunType, ResultStatus, ResultJson, StartedOn, CompletedOn, RunBy
    F
ROM dbo.API_Config_CheckRun
    WHERE ConnectorVersionId = @ConnectorVersionId
      AND (@RunType IS NULL OR RunType = @RunType)
    ORDER BY StartedOn DESC;
END


Text
---------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------

/* ============================================================================
   CHECK RUNS - Validate Configuration / Connection Test evidence
   ============================================================================ */
CREATE PROCEDURE dbo.API_
Config_CheckRun_Insert
    @ConnectorVersionId INT,
    @RunType            NVARCHAR(30),
    @ResultStatus       NVARCHAR(20),
    @ResultJson         NVARCHAR(MAX) = NULL,
    @StartedOn       DATETIME  = NULL,
    @RunBy              NVARCHAR(128) = NU
LL
AS
BEGIN
    SET NOCOUNT ON;
    INSERT INTO dbo.API_Config_CheckRun
        (ConnectorVersionId, RunType, ResultStatus, ResultJson, StartedOn, CompletedOn, RunBy)
    VALUES
        (@ConnectorVersionId, @RunType, @ResultStatus, @ResultJson,
         
ISNULL(@StartedOn, GETDATE()), GETDATE(), @RunBy);
END


Text
---------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------

/* ============================================================================
   CONNECTOR CATALOG
   ============================================================================ */
CREATE PROCEDURE dbo.API_Config_Connector_GetCatalog
    @Search     N
VARCHAR(200)    = NULL,
    @CustomerId INT = NULL,
    @Status     NVARCHAR(20)     = NULL
AS
BEGIN
    SET NOCOUNT ON;

    SELECT
        v.ConnectorVersionId,
        c.ConnectorId,
        c.ConnectorName,
        c.ConnectorCode,
        c.Operation
Code,
        c.Enabled,
        cu.CustomerName,
        a.ApplicationName,
        v.VersionNumber,
        v.Status,
        v.Protocol,
        v.HttpMethod,
        v.BaseUrl,
        v.EndpointTemplate,
        v.ConfigHash,
        v.PublishedOn,
 
       v.ModifiedOn,
        AuthType = ISNULL(au.AuthType, N'None'),
        IsCurrentPublished = CASE WHEN c.CurrentPublishedVersionId = v.ConnectorVersionId THEN 1 ELSE 0 END
    FROM dbo.API_Config_ConnectorVersion v
    INNER JOIN dbo.API_Config_Conn
ector c    ON c.ConnectorId = v.ConnectorId
    INNER JOIN dbo.API_Config_Customer cu    ON cu.CustomerId = c.CustomerId
    INNER JOIN dbo.API_Config_Application a  ON a.ApplicationId = c.ApplicationId
    LEFT  JOIN dbo.API_Config_AuthConfig au  ON au.C
onnectorVersionId = v.ConnectorVersionId
    WHERE (@CustomerId IS NULL OR c.CustomerId = @CustomerId)
      AND (@Status IS NULL OR @Status = N'' OR v.Status = @Status)
      AND (@Search IS NULL OR @Search = N''
           OR c.ConnectorName LIKE N'%' +
 @Search + N'%'
           OR c.ConnectorCode LIKE N'%' + @Search + N'%'
           OR c.OperationCode LIKE N'%' + @Search + N'%'
           OR cu.CustomerName LIKE N'%' + @Search + N'%')
    ORDER BY c.ConnectorName, v.VersionNumber DESC;
END


Text
---------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------

/* Published connectors only - used by the simulator dropdown */
CREATE PROCEDURE dbo.API_Config_Connector_GetPublishedList
AS
BEGIN
    SET NOCOUNT ON;
    SELECT
        v.ConnectorVersionId,
        c.ConnectorId,
        c.ConnectorName,
        c.Co
nnectorCode,
        c.OperationCode,
        v.VersionNumber,
        v.HttpMethod,
        v.BaseUrl,
        v.EndpointTemplate,
        v.ExecutionModeAllowed,
        v.SampleSourceJson,
        cu.CustomerName
    FROM dbo.API_Config_Connector c
   
 INNER JOIN dbo.API_Config_ConnectorVersion v ON v.ConnectorVersionId = c.CurrentPublishedVersionId
    INNER JOIN dbo.API_Config_Customer cu        ON cu.CustomerId = c.CustomerId
    WHERE c.Enabled = 1
    ORDER BY c.ConnectorName;
END


Text
---------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------

CREATE PROCEDURE dbo.API_Config_Connector_SetActive
    @ConnectorId INT,
    @Enabled     BIT,
    @ActorName   NVARCHAR(128) = NULL
AS
BEGIN
    SET NOCOUNT ON;

    UPDATE dbo.API_Config_Connector
       SET Enabled    = @Enabled,
           ModifiedB
y = @ActorName,
           ModifiedOn = GETDATE()
     WHERE ConnectorId = @ConnectorId;

    EXEC dbo.API_Config_Audit_Insert
        @EventType  = N'CONNECTOR_ACTIVE_CHANGED',
        @EntityType = N'Connector',
        @EntityId   = @ConnectorId,
     
   @ActorName  = @ActorName,
        @DetailsJson= NULL;
END


Text
---------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------

/* ============================================================================
   CLONE A PUBLISHED VERSION INTO A NEW DRAFT
   ============================================================================ */
CREATE PROCEDURE dbo.API_Config_ConnectorVers
ion_Clone
    @SourceVersionId INT,
    @ActorName       NVARCHAR(128) = NULL,
    @NewVersionId    INT OUTPUT
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;

    DECLARE @connectorId INT, @versionNo INT;

    SELECT @connectorId = ConnectorId FROM d
bo.API_Config_ConnectorVersion WHERE ConnectorVersionId = @SourceVersionId;
    IF @connectorId IS NULL
    BEGIN
        RAISERROR (N'API_Config: source version not found.', 16, 1);
        RETURN;
    END

    BEGIN TRY
        BEGIN TRANSACTION;

     
   SELECT @versionNo = ISNULL(MAX(VersionNumber), 0) + 1
        FROM dbo.API_Config_ConnectorVersion WITH (UPDLOCK, HOLDLOCK)
        WHERE ConnectorId = @connectorId;

        INSERT INTO dbo.API_Config_ConnectorVersion
            (ConnectorId, Version
Number, Status, Protocol, ExecutionModeAllowed,
             HttpMethod, BaseUrlAlias, BaseUrl, EndpointTemplate, SampleSourceJson, PayloadTemplateJson,
             TimeoutSeconds, MaxAttempts, IdempotencyHeader, CorrelationHeader, ResponseType,
        
     ResultMappingJson, IsRetrySafe, CreatedBy, ModifiedOn, ModifiedBy)
        SELECT ConnectorId, @versionNo, N'Draft', Protocol, ExecutionModeAllowed,
               HttpMethod, BaseUrlAlias, BaseUrl, EndpointTemplate, SampleSourceJson, PayloadTemplate
Json,
               TimeoutSeconds, MaxAttempts, IdempotencyHeader, CorrelationHeader, ResponseType,
               ResultMappingJson, IsRetrySafe, @ActorName, GETDATE(), @ActorName
        FROM dbo.API_Config_ConnectorVersion
        WHERE ConnectorVers
ionId = @SourceVersionId;

        SET @NewVersionId = CONVERT(INT, SCOPE_IDENTITY());

        INSERT INTO dbo.API_Config_Header (ConnectorVersionId, HeaderName, HeaderValueTemplate, SortOrder)
        SELECT @NewVersionId, HeaderName, HeaderValueTemplat
e, SortOrder
        FROM dbo.API_Config_Header WHERE ConnectorVersionId = @SourceVersionId;

        INSERT INTO dbo.API_Config_QueryParameter (ConnectorVersionId, ParameterName, ParameterValueTemplate, SortOrder)
        SELECT @NewVersionId, ParameterN
ame, ParameterValueTemplate, SortOrder
        FROM dbo.API_Config_QueryParameter WHERE ConnectorVersionId = @SourceVersionId;

        INSERT INTO dbo.API_Config_AllowedHost (ConnectorVersionId, HostName, PortNumber)
        SELECT @NewVersionId, HostNam
e, PortNumber
        FROM dbo.API_Config_AllowedHost WHERE ConnectorVersionId = @SourceVersionId;

        INSERT INTO dbo.API_Config_SuccessCode (ConnectorVersionId, StatusCode)
        SELECT @NewVersionId, StatusCode
        FROM dbo.API_Config_Succes
sCode WHERE ConnectorVersionId = @SourceVersionId;

        INSERT INTO dbo.API_Config_AuthConfig
            (ConnectorVersionId, AuthType, SecretStoreAlias, ClientIdSecretRef, ClientSecretSecretRef,
             TokenEndpoint, Scope, Audience, ApiKeyNam
e, ApiKeyLocation, ApiKeySecretRef,
             BearerTokenSecretRef, UsernameSecretRef, PasswordSecretRef, CertificateSecretRef,
             CustomPluginKey, AdditionalSettingsJson)
        SELECT @NewVersionId, AuthType, SecretStoreAlias, ClientIdSecr
etRef, ClientSecretSecretRef,
               TokenEndpoint, Scope, Audience, ApiKeyName, ApiKeyLocation, ApiKeySecretRef,
               BearerTokenSecretRef, UsernameSecretRef, PasswordSecretRef, CertificateSecretRef,
               CustomPluginKey, Addi
tionalSettingsJson
        FROM dbo.API_Config_AuthConfig WHERE ConnectorVersionId = @SourceVersionId;

        EXEC dbo.API_Config_Audit_Insert
            @EventType   = N'CONNECTOR_VERSION_CLONED',
            @EntityType  = N'ConnectorVersion',
      
      @EntityId    = @NewVersionId,
            @ActorName   = @ActorName,
            @DetailsJson = NULL;

        COMMIT TRANSACTION;
    END TRY
    BEGIN CATCH
        IF XACT_STATE() <> 0 ROLLBACK TRANSACTION;
        THROW;
    END CATCH

    SELEC
T NewVersionId = @NewVersionId, VersionNumber = @versionNo;
END


Text
---------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------

/* ============================================================================
   LOAD ONE VERSION - 6 result sets consumed by the wizard
   ============================================================================ */
CREATE PROCEDURE dbo.API_Config_
ConnectorVersion_Get
    @ConnectorVersionId INT
AS
BEGIN
    SET NOCOUNT ON;

    /* 0 - version + connector header */
    SELECT
        v.ConnectorVersionId, v.ConnectorId, v.VersionNumber, v.Status,
        v.Protocol, v.ExecutionModeAllowed, v.HttpMe
thod, v.BaseUrlAlias, v.BaseUrl,
        v.EndpointTemplate, v.SampleSourceJson, v.PayloadTemplateJson,
        v.TimeoutSeconds, v.MaxAttempts, v.IdempotencyHeader, v.CorrelationHeader,
        v.ResponseType, v.ResultMappingJson, v.IsRetrySafe,
        
v.PublishedSnapshotJson, v.ConfigHash, v.PublishedOn, v.PublishedBy,
        --RowVersionHex = CONVERT(NVARCHAR(50), v.RowVersion, 1),
        RowVersionHex = CONVERT(NVARCHAR(50), CONVERT(VARBINARY(8), v.RowVersion), 1),
        c.CustomerId, c.Applicati
onId, c.ConnectorCode, c.ConnectorName, c.OperationCode, c.Enabled,
        cu.CustomerName, a.ApplicationName
    FROM dbo.API_Config_ConnectorVersion v
    INNER JOIN dbo.API_Config_Connector c   ON c.ConnectorId = v.ConnectorId
    INNER JOIN dbo.API_C
onfig_Customer cu   ON cu.CustomerId = c.CustomerId
    INNER JOIN dbo.API_Config_Application a ON a.ApplicationId = c.ApplicationId
    WHERE v.ConnectorVersionId = @ConnectorVersionId;

    /* 1 - headers */
    SELECT HeaderName, HeaderValueTemplate, S
ortOrder
    FROM dbo.API_Config_Header
    WHERE ConnectorVersionId = @ConnectorVersionId AND Enabled = 1
    ORDER BY SortOrder, HeaderName;

    /* 2 - query parameters */
    SELECT ParameterName, ParameterValueTemplate, SortOrder
    FROM dbo.API_Con
fig_QueryParameter
    WHERE ConnectorVersionId = @ConnectorVersionId AND Enabled = 1
    ORDER BY SortOrder, ParameterName;

    /* 3 - allowed hosts */
    SELECT HostName, PortNumber
    FROM dbo.API_Config_AllowedHost
    WHERE ConnectorVersionId = @C
onnectorVersionId AND Enabled = 1
    ORDER BY HostName;

    /* 4 - success codes */
    SELECT StatusCode
    FROM dbo.API_Config_SuccessCode
    WHERE ConnectorVersionId = @ConnectorVersionId AND Enabled = 1
    ORDER BY StatusCode;

    /* 5 - auth co
nfiguration (references only) */
    SELECT AuthType, SecretStoreAlias, ClientIdSecretRef, ClientSecretSecretRef,
           TokenEndpoint, Scope, Audience, ApiKeyName, ApiKeyLocation, ApiKeySecretRef,
           BearerTokenSecretRef, UsernameSecretRef, P
asswordSecretRef,
           CertificateSecretRef, CustomPluginKey, AdditionalSettingsJson
    FROM dbo.API_Config_AuthConfig
    WHERE ConnectorVersionId = @ConnectorVersionId AND Enabled = 1;

    /* 6 - last check runs */
    SELECT TOP (5) RunType, Re
sultStatus, ResultJson, StartedOn, CompletedOn, RunBy
    FROM dbo.API_Config_CheckRun
    WHERE ConnectorVersionId = @ConnectorVersionId
    ORDER BY StartedOn DESC;
END


Text
---------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------

/* ============================================================================
   PUBLISH - freeze the snapshot, compute SHA-256 ConfigHash, mark immutable
   ============================================================================ */
CREATE PROCEDU
RE dbo.API_Config_ConnectorVersion_Publish
    @ConnectorVersionId    INT,
    @ActorName             NVARCHAR(128) = NULL,
    @RequireConnectionTest BIT           = 0
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;

    DECLARE @status NVARCHAR(20),
 @connectorId INT;

    SELECT @status = v.Status, @connectorId = v.ConnectorId
    FROM dbo.API_Config_ConnectorVersion v
    WHERE v.ConnectorVersionId = @ConnectorVersionId;

    IF @status IS NULL
    BEGIN
        RAISERROR (N'API_Config: connector v
ersion not found.', 16, 1);
        RETURN;
    END
    IF @status <> N'Draft'
    BEGIN
        RAISERROR (N'API_Config: only a Draft version can be published.', 16, 1);
        RETURN;
    END

    /* ---- server-side revalidation of the publish rules -
----------------- */
    DECLARE @errors NVARCHAR(MAX) = N'';

    SELECT @errors = @errors +
        CASE WHEN NULLIF(LTRIM(RTRIM(v.HttpMethod)), N'') IS NULL      THEN N'HTTP method is required. ' ELSE N'' END +
        CASE WHEN NULLIF(LTRIM(RTRIM(v.Ba
seUrl)), N'') IS NULL         THEN N'Base URL is required. ' ELSE N'' END +
        CASE WHEN v.BaseUrl IS NOT NULL AND LOWER(LEFT(v.BaseUrl, 8)) <> N'https://'
                                                                        THEN N'Base URL must u
se HTTPS. ' ELSE N'' END +
        CASE WHEN NULLIF(LTRIM(RTRIM(v.EndpointTemplate)), N'') IS NULL THEN N'Relative endpoint template is required. ' ELSE N'' END +
        CASE WHEN v.EndpointTemplate IS NOT NULL AND LEFT(v.EndpointTemplate, 1) <> N'/'
   
                                                                     THEN N'Endpoint template must start with a forward slash. ' ELSE N'' END +
        CASE WHEN v.Protocol <> N'REST'                                 THEN N'Only the REST executor is availa
ble in this release. ' ELSE N'' END +
        CASE WHEN v.PayloadTemplateJson IS NOT NULL AND ISJSON(v.PayloadTemplateJson) = 0
                                                                        THEN N'Payload template is not valid JSON. ' ELSE N'' E
ND +
        CASE WHEN NOT EXISTS (SELECT 1 FROM dbo.API_Config_SuccessCode s WHERE s.ConnectorVersionId = v.ConnectorVersionId AND s.Enabled = 1)
                                                                        THEN N'At least one success status c
ode is required. ' ELSE N'' END +
        CASE WHEN NOT EXISTS (SELECT 1 FROM dbo.API_Config_AllowedHost h WHERE h.ConnectorVersionId = v.ConnectorVersionId AND h.Enabled = 1)
                                                                        THEN N'
At least one approved host is required. ' ELSE N'' END
    FROM dbo.API_Config_ConnectorVersion v
    WHERE v.ConnectorVersionId = @ConnectorVersionId;

    /* the base URL host must appear in the allowlist */
    DECLARE @baseUrl NVARCHAR(500), @baseHost
 NVARCHAR(255);
    SELECT @baseUrl = BaseUrl FROM dbo.API_Config_ConnectorVersion WHERE ConnectorVersionId = @ConnectorVersionId;

    IF @baseUrl IS NOT NULL AND CHARINDEX(N'://', @baseUrl) > 0
    BEGIN
        SET @baseHost = SUBSTRING(@baseUrl, CHARI
NDEX(N'://', @baseUrl) + 3, 500);
        IF CHARINDEX(N'/', @baseHost) > 0 SET @baseHost = LEFT(@baseHost, CHARINDEX(N'/', @baseHost) - 1);
        IF CHARINDEX(N':', @baseHost) > 0 SET @baseHost = LEFT(@baseHost, CHARINDEX(N':', @baseHost) - 1);
       
 SET @baseHost = LOWER(@baseHost);

        IF NOT EXISTS (SELECT 1 FROM dbo.API_Config_AllowedHost
                       WHERE ConnectorVersionId = @ConnectorVersionId AND HostName = @baseHost AND Enabled = 1)
            SET @errors = @errors + N'The b
ase URL host is not in the approved host allowlist. ';
    END

    IF @RequireConnectionTest = 1
       AND NOT EXISTS (SELECT 1 FROM dbo.API_Config_CheckRun
        WHERE ConnectorVersionId = @ConnectorVersionId
                         AND RunType = N'
ConnectionTest'
                         AND ResultStatus = N'Passed'
                         AND StartedOn >= DATEADD(DAY, -7, GETDATE()))
        SET @errors = @errors + N'A passing connection test within the last 7 days is required. ';

    IF LEN(@er
rors) > 0
    BEGIN
        /* RAISERROR substitution arguments cannot be an NVARCHAR(MAX) value. */
        DECLARE @errorText NVARCHAR(2000) = LEFT(@errors, 2000);
        RAISERROR (N'API_Config publish blocked: %s', 16, 1, @errorText);
        RETURN;

    END

    BEGIN TRY
        BEGIN TRANSACTION;

        /* ---- canonical snapshot (deterministic ordering) ---------------- */
        DECLARE @snapshot NVARCHAR(MAX);

        SET @snapshot =
        (
            SELECT
                v.ConnectorV
ersionId,
                c.ConnectorCode,
                c.ConnectorName,
                c.OperationCode,
                v.VersionNumber,
                v.Protocol,
                v.ExecutionModeAllowed,
                v.HttpMethod,
               
 v.BaseUrlAlias,
                v.BaseUrl,
                v.EndpointTemplate,
                v.TimeoutSeconds,
                v.MaxAttempts,
                v.IdempotencyHeader,
                v.CorrelationHeader,
                v.ResponseType,
    
            v.IsRetrySafe,
                PayloadTemplate = v.PayloadTemplateJson,
                ResultMapping   = v.ResultMappingJson,
                Headers = (SELECT h.HeaderName AS [name], h.HeaderValueTemplate AS [value]
                         
  FROM dbo.API_Config_Header h
                           WHERE h.ConnectorVersionId = v.ConnectorVersionId AND h.Enabled = 1
                           ORDER BY h.HeaderName, h.HeaderValueTemplate
                           FOR JSON PATH),
              
  QueryParameters = (SELECT q.ParameterName AS [name], q.ParameterValueTemplate AS [value]
                           FROM dbo.API_Config_QueryParameter q
                           WHERE q.ConnectorVersionId = v.ConnectorVersionId AND q.Enabled = 1
     
                      ORDER BY q.ParameterName, q.ParameterValueTemplate
                           FOR JSON PATH),
                AllowedHosts = (SELECT a.HostName AS [host], a.PortNumber AS [port]
                           FROM dbo.API_Config_AllowedH
ost a
                           WHERE a.ConnectorVersionId = v.ConnectorVersionId AND a.Enabled = 1
                           ORDER BY a.HostName
                           FOR JSON PATH),
                SuccessCodes = (SELECT s.StatusCode AS [code]
  
                         FROM dbo.API_Config_SuccessCode s
                           WHERE s.ConnectorVersionId = v.ConnectorVersionId AND s.Enabled = 1
                           ORDER BY s.StatusCode
                           FOR JSON PATH),
         
       Auth = (SELECT au.AuthType, au.SecretStoreAlias, au.ClientIdSecretRef, au.ClientSecretSecretRef,
                               au.TokenEndpoint, au.Scope, au.Audience, au.ApiKeyName, au.ApiKeyLocation,
                               au.ApiKeySecre
tRef, au.BearerTokenSecretRef, au.UsernameSecretRef,
                               au.PasswordSecretRef, au.CertificateSecretRef, au.CustomPluginKey
                        FROM dbo.API_Config_AuthConfig au
                        WHERE au.ConnectorVersi
onId = v.ConnectorVersionId AND au.Enabled = 1
                        FOR JSON PATH, WITHOUT_ARRAY_WRAPPER)
            FROM dbo.API_Config_ConnectorVersion v
            INNER JOIN dbo.API_Config_Connector c ON c.ConnectorId = v.ConnectorId
            
WHERE v.ConnectorVersionId = @ConnectorVersionId
            FOR JSON PATH, WITHOUT_ARRAY_WRAPPER
        );

        DECLARE @configHash CHAR(64) =
            CONVERT(CHAR(64), HASHBYTES('SHA2_256', @snapshot), 2);

        UPDATE dbo.API_Config_Connect
orVersion
           SET Status                = N'Published',
               PublishedSnapshotJson = @snapshot,
               ConfigHash            = @configHash,
               PublishedOn        = GETDATE(),
               PublishedBy           = @Act
orName,
               ModifiedOn         = GETDATE(),
               ModifiedBy            = @ActorName
         WHERE ConnectorVersionId = @ConnectorVersionId;

        UPDATE dbo.API_Config_Connector
           SET CurrentPublishedVersionId = @Connecto
rVersionId
         WHERE ConnectorId = @connectorId;

        EXEC dbo.API_Config_Audit_Insert
            @EventType   = N'CONNECTOR_VERSION_PUBLISHED',
            @EntityType  = N'ConnectorVersion',
            @EntityId    = @ConnectorVersionId,
    
        @ActorName   = @ActorName,
            @DetailsJson = @configHash;

        COMMIT TRANSACTION;
    END TRY
    BEGIN CATCH
        IF XACT_STATE() <> 0 ROLLBACK TRANSACTION;
        THROW;
    END CATCH

    SELECT ConnectorVersionId = @Connector
VersionId,
           ConfigHash         = @configHash,
           Status             = N'Published';
END


Text
---------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------

/* ============================================================================
   SAVE DRAFT - creates or updates a Draft version in one transaction
   Child collections arrive as JSON arrays, success codes as a CSV string.
   ==========================
================================================== */
CREATE PROCEDURE dbo.API_Config_ConnectorVersion_SaveDraft
    @ConnectorVersionId   INT = NULL,
    @CustomerId           INT,
    @ApplicationId        INT,
    @ConnectorCode        NVARCHAR(100),
 
   @ConnectorName        NVARCHAR(200),
    @OperationCode        NVARCHAR(100),
    @Protocol             NVARCHAR(20)   = N'REST',
    @ExecutionModeAllowed NVARCHAR(30)   = N'Both',
    @HttpMethod           NVARCHAR(10)   = NULL,
    @BaseUrlAlias    
     NVARCHAR(100)  = NULL,
    @BaseUrl              NVARCHAR(500)  = NULL,
    @EndpointTemplate     NVARCHAR(500)  = NULL,
    @SampleSourceJson     NVARCHAR(MAX)  = NULL,
    @PayloadTemplateJson  NVARCHAR(MAX)  = NULL,
    @TimeoutSeconds       INT  
          = 30,
    @MaxAttempts          INT            = 3,
    @IdempotencyHeader    NVARCHAR(100)  = NULL,
    @CorrelationHeader    NVARCHAR(100)  = NULL,
    @ResponseType         NVARCHAR(20)   = N'JSON',
    @ResultMappingJson    NVARCHAR(MAX)  = 
NULL,
    @IsRetrySafe          BIT            = 0,
    @AuthJson             NVARCHAR(MAX)  = NULL,   -- single object
    @HeadersJson          NVARCHAR(MAX)  = NULL,   -- [{"name":"","value":""}]
    @QueryParamsJson      NVARCHAR(MAX)  = NULL,   -- [{
"name":"","value":""}]
    @AllowedHostsJson     NVARCHAR(MAX)  = NULL,   -- [{"host":"","port":null}]
    @SuccessCodesCsv      NVARCHAR(200)  = NULL,   -- "200,201,202"
    @RowVersionHex        NVARCHAR(50)   = NULL,   -- optimistic concurrency token, 
e.g. 0x00000000000007D1
    @ActorName            NVARCHAR(128)  = NULL,
    @NewConnectorVersionId INT OUTPUT,
    @NewConnectorId        INT OUTPUT,
    @VersionNumberOut      INT              OUTPUT
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;


    DECLARE @connectorId INT;
    DECLARE @versionId   INT = @ConnectorVersionId;
    DECLARE @versionNo   INT;

    /* -- guard rails ---------------------------------------------------- */
    IF NOT EXISTS (SELECT 1 FROM dbo.API_Config_Application
    
               WHERE ApplicationId = @ApplicationId AND CustomerId = @CustomerId AND Enabled = 1)
    BEGIN
        RAISERROR (N'API_Config: the selected application does not belong to the selected customer.', 16, 1);
        RETURN;
    END

    IF @vers
ionId IS NOT NULL
       AND EXISTS (SELECT 1 FROM dbo.API_Config_ConnectorVersion
                   WHERE ConnectorVersionId = @versionId AND Status = N'Published')
    BEGIN
        RAISERROR (N'API_Config: a Published version cannot be edited. Clone i
t as a new Draft version.', 16, 1);
        RETURN;
    END

    BEGIN TRY
        BEGIN TRANSACTION;

        IF @versionId IS NULL
        BEGIN
            /* Re-use the logical connector when the code already exists for the customer. */
            SE
LECT @connectorId = ConnectorId
            FROM dbo.API_Config_Connector
            WHERE CustomerId = @CustomerId AND ConnectorCode = @ConnectorCode;

            IF @connectorId IS NULL
            BEGIN
                INSERT INTO dbo.API_Config_Conn
ector
                    (CustomerId, ApplicationId, ConnectorCode, ConnectorName, OperationCode, CreatedBy)
                VALUES
                    (@CustomerId, @ApplicationId, @ConnectorCode, @ConnectorName, @OperationCode, @ActorName);

          
      SET @connectorId = CONVERT(INT, SCOPE_IDENTITY());
            END
            ELSE
            BEGIN
                UPDATE dbo.API_Config_Connector
                   SET ApplicationId = @ApplicationId,
                       ConnectorName = @Conn
ectorName,
                       OperationCode = @OperationCode,
                       ModifiedBy    = @ActorName,
                       ModifiedOn    = GETDATE()
         WHERE ConnectorId = @connectorId;
            END

            SELECT @versionNo
 = ISNULL(MAX(VersionNumber), 0) + 1
            FROM dbo.API_Config_ConnectorVersion WITH (UPDLOCK, HOLDLOCK)
            WHERE ConnectorId = @connectorId;

            INSERT INTO dbo.API_Config_ConnectorVersion
                (ConnectorId, VersionNumb
er, Status, Protocol, ExecutionModeAllowed,
                 HttpMethod, BaseUrlAlias, BaseUrl, EndpointTemplate, SampleSourceJson, PayloadTemplateJson,
                 TimeoutSeconds, MaxAttempts, IdempotencyHeader, CorrelationHeader, ResponseType,
    
             ResultMappingJson, IsRetrySafe, CreatedBy, ModifiedOn, ModifiedBy)
            VALUES
                (@connectorId, @versionNo, N'Draft', @Protocol, @ExecutionModeAllowed,
                 @HttpMethod, @BaseUrlAlias, @BaseUrl, @EndpointTempl
ate, @SampleSourceJson, @PayloadTemplateJson,
                 @TimeoutSeconds, @MaxAttempts, @IdempotencyHeader, @CorrelationHeader, @ResponseType,
                 @ResultMappingJson, @IsRetrySafe, @ActorName, GETDATE(), @ActorName);

            SET @v
ersionId = CONVERT(INT, SCOPE_IDENTITY());
        END
        ELSE
        BEGIN
            SELECT @connectorId = ConnectorId, @versionNo = VersionNumber
            FROM dbo.API_Config_ConnectorVersion WITH (UPDLOCK)
            WHERE ConnectorVersionI
d = @versionId;

            IF @connectorId IS NULL
            BEGIN
                RAISERROR (N'API_Config: connector version not found.', 16, 1);
                ROLLBACK TRANSACTION;
                RETURN;
            END

            /* Optimistic
 concurrency: another user must not be silently overwritten. */
            IF NULLIF(@RowVersionHex, N'') IS NOT NULL
               AND NOT EXISTS (SELECT 1 FROM dbo.API_Config_ConnectorVersion
                               WHERE ConnectorVersionId = @
versionId
                                 AND RowVersion = CONVERT(VARBINARY(8), @RowVersionHex, 1))
            BEGIN
                RAISERROR (N'API_Config: this draft was changed by another user after you loaded it. Reload the draft and re-apply your
 changes.', 16, 1);
                ROLLBACK TRANSACTION;
                RETURN;
            END

            UPDATE dbo.API_Config_Connector
               SET CustomerId    = @CustomerId,
                   ApplicationId = @ApplicationId,
             
      ConnectorCode = @ConnectorCode,
                   ConnectorName = @ConnectorName,
                   OperationCode = @OperationCode,
                   ModifiedBy    = @ActorName,
                   ModifiedOn    = GETDATE()
             WHERE Conn
ectorId = @connectorId;

            UPDATE dbo.API_Config_ConnectorVersion
               SET Protocol             = @Protocol,
                   ExecutionModeAllowed = @ExecutionModeAllowed,
                   HttpMethod           = @HttpMethod,
      
             BaseUrlAlias         = @BaseUrlAlias,
                   BaseUrl              = @BaseUrl,
                   EndpointTemplate     = @EndpointTemplate,
                   SampleSourceJson     = @SampleSourceJson,
                   PayloadTemp
lateJson  = @PayloadTemplateJson,
                   TimeoutSeconds       = @TimeoutSeconds,
                   MaxAttempts          = @MaxAttempts,
                   IdempotencyHeader    = @IdempotencyHeader,
                   CorrelationHeader    = @C
orrelationHeader,
                   ResponseType         = @ResponseType,
                   ResultMappingJson    = @ResultMappingJson,
                   IsRetrySafe          = @IsRetrySafe,
                   ModifiedOn        = GETDATE(),
            
       ModifiedBy           = @ActorName
             WHERE ConnectorVersionId = @versionId;
        END

        /* -- child rows: delete and re-insert (low volume configuration) --- */
        DELETE FROM dbo.API_Config_Header         WHERE ConnectorVer
sionId = @versionId;
        DELETE FROM dbo.API_Config_QueryParameter WHERE ConnectorVersionId = @versionId;
        DELETE FROM dbo.API_Config_AllowedHost    WHERE ConnectorVersionId = @versionId;
        DELETE FROM dbo.API_Config_SuccessCode    WHERE 
ConnectorVersionId = @versionId;
        DELETE FROM dbo.API_Config_AuthConfig     WHERE ConnectorVersionId = @versionId;

        IF @HeadersJson IS NOT NULL AND ISJSON(@HeadersJson) = 1
            INSERT INTO dbo.API_Config_Header (ConnectorVersionId, 
HeaderName, HeaderValueTemplate, SortOrder)
            SELECT @versionId, j.[name], j.[value], j.[key]
            FROM OPENJSON(@HeadersJson)
                 WITH ([name] NVARCHAR(100) N'$.name',
                       [value] NVARCHAR(500) N'$.value',

                       [key] INT N'$.sort') j
            WHERE NULLIF(LTRIM(RTRIM(j.[name])), N'') IS NOT NULL
              /* hop-by-hop and restricted headers are rejected */
              AND LOWER(j.[name]) NOT IN (N'host', N'content-length', N'con
nection',
                                          N'transfer-encoding', N'upgrade', N'keep-alive',
                                          N'proxy-authorization', N'te', N'trailer');

        IF @QueryParamsJson IS NOT NULL AND ISJSON(@QueryParamsJson
) = 1
            INSERT INTO dbo.API_Config_QueryParameter (ConnectorVersionId, ParameterName, ParameterValueTemplate, SortOrder)
            SELECT @versionId, j.[name], j.[value], j.[key]
            FROM OPENJSON(@QueryParamsJson)
                 WIT
H ([name] NVARCHAR(100) N'$.name',
                       [value] NVARCHAR(500) N'$.value',
                       [key] INT N'$.sort') j
            WHERE NULLIF(LTRIM(RTRIM(j.[name])), N'') IS NOT NULL;

        IF @AllowedHostsJson IS NOT NULL AND ISJS
ON(@AllowedHostsJson) = 1
            INSERT INTO dbo.API_Config_AllowedHost (ConnectorVersionId, HostName, PortNumber)
            SELECT DISTINCT @versionId, LOWER(LTRIM(RTRIM(j.[host]))), j.[port]
            FROM OPENJSON(@AllowedHostsJson)
          
       WITH ([host] NVARCHAR(255) N'$.host',
                       [port] INT N'$.port') j
            WHERE NULLIF(LTRIM(RTRIM(j.[host])), N'') IS NOT NULL;

        IF NULLIF(LTRIM(RTRIM(@SuccessCodesCsv)), N'') IS NOT NULL
            INSERT INTO dbo.
API_Config_SuccessCode (ConnectorVersionId, StatusCode)
            SELECT DISTINCT @versionId, TRY_CONVERT(INT, LTRIM(RTRIM(s.[value])))
            FROM STRING_SPLIT(@SuccessCodesCsv, N',') s
            WHERE TRY_CONVERT(INT, LTRIM(RTRIM(s.[value]))) B
ETWEEN 100 AND 599;

        IF @AuthJson IS NOT NULL AND ISJSON(@AuthJson) = 1
            INSERT INTO dbo.API_Config_AuthConfig
                (ConnectorVersionId, AuthType, SecretStoreAlias, ClientIdSecretRef, ClientSecretSecretRef,
                 T
okenEndpoint, Scope, Audience, ApiKeyName, ApiKeyLocation, ApiKeySecretRef,
                 BearerTokenSecretRef, UsernameSecretRef, PasswordSecretRef, CertificateSecretRef,
                 CustomPluginKey, AdditionalSettingsJson)
            SELECT @ve
rsionId, ISNULL(j.AuthType, N'None'), j.SecretStoreAlias, j.ClientIdSecretRef,
                   j.ClientSecretSecretRef, j.TokenEndpoint, j.Scope, j.Audience, j.ApiKeyName,
                   j.ApiKeyLocation, j.ApiKeySecretRef, j.BearerTokenSecretRef, 
j.UsernameSecretRef,
                   j.PasswordSecretRef, j.CertificateSecretRef, j.CustomPluginKey, j.AdditionalSettingsJson
            FROM OPENJSON(@AuthJson)
                 WITH (AuthType NVARCHAR(50), SecretStoreAlias NVARCHAR(100),
           
            ClientIdSecretRef NVARCHAR(300), ClientSecretSecretRef NVARCHAR(300),
                       TokenEndpoint NVARCHAR(500), Scope NVARCHAR(300), Audience NVARCHAR(300),
                       ApiKeyName NVARCHAR(100), ApiKeyLocation NVARCHAR(20)
, ApiKeySecretRef NVARCHAR(300),
                       BearerTokenSecretRef NVARCHAR(300), UsernameSecretRef NVARCHAR(300),
                       PasswordSecretRef NVARCHAR(300), CertificateSecretRef NVARCHAR(300),
                       CustomPluginKey
 NVARCHAR(100), AdditionalSettingsJson NVARCHAR(MAX) N'$.AdditionalSettingsJson' AS JSON) j;

        EXEC dbo.API_Config_Audit_Insert
            @EventType   = N'CONNECTOR_DRAFT_SAVED',
            @EntityType  = N'ConnectorVersion',
            @Entity
Id    = @versionId,
            @ActorName   = @ActorName,
            @DetailsJson = NULL;

        COMMIT TRANSACTION;
    END TRY
    BEGIN CATCH
        IF XACT_STATE() <> 0 ROLLBACK TRANSACTION;
        THROW;
    END CATCH

    SET @NewConnectorVers
ionId = @versionId;
    SET @NewConnectorId        = @connectorId;
    SET @VersionNumberOut      = @versionNo;

    SELECT ConnectorVersionId = @versionId,
           ConnectorId        = @connectorId,
           VersionNumber      = @versionNo;
END


Text
---------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------

/* ============================================================================
   MASTER DATA LOOKUPS
   ============================================================================ */
CREATE PROCEDURE dbo.API_Config_Customer_GetList
AS
BEGIN
    SET NO
COUNT ON;
    SELECT CustomerId, CustomerCode, CustomerName
    FROM dbo.API_Config_Customer
    WHERE Enabled = 1
    ORDER BY CustomerName;
END


Text
---------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------

/* ============================================================================
   DASHBOARD
   ============================================================================ */
CREATE PROCEDURE dbo.API_Config_Dashboard_GetMetrics
    @ReportingDays INT = 
30
AS
BEGIN
    SET NOCOUNT ON;

    DECLARE @from DATETIME = DATEADD(DAY, -@ReportingDays, GETDATE());

    SELECT
        PublishedConnectors = (SELECT COUNT(*) FROM dbo.API_Config_Connector
                               WHERE Enabled = 1 AND CurrentPu
blishedVersionId IS NOT NULL),
        DraftConnectors     = (SELECT COUNT(*) FROM dbo.API_Config_ConnectorVersion
                               WHERE Status = N'Draft'),
        PendingApprovals    = (SELECT COUNT(*) FROM dbo.API_Config_ExecutionApprova
l
                               WHERE Status = N'Pending'),
        SuccessfulExecutions= (SELECT COUNT(*) FROM dbo.API_Config_ExecutionRequest
                               WHERE Status = N'Succeeded' AND CreatedOn >= @from),
        FailedExecutions  
  = (SELECT COUNT(*) FROM dbo.API_Config_ExecutionRequest
                               WHERE Status IN (N'BusinessFailed', N'TechnicalFailed', N'UnknownOutcome')
                                 AND CreatedOn >= @from),
        OpenExceptions      = (SE
LECT COUNT(*) FROM dbo.API_Config_ExecutionException
                               WHERE Status = N'Open');

    /* Second result set - recent activity strip */
    SELECT TOP (8)
           e.ExecutionId,
           e.ExecutionUid,
           c.Connecto
rName,
           v.VersionNumber,
           e.Status,
           e.ExecutionMode,
           e.CreatedOn
    FROM dbo.API_Config_ExecutionRequest e
    INNER JOIN dbo.API_Config_Connector c        ON c.ConnectorId = e.ConnectorId
    INNER JOIN dbo.API_
Config_ConnectorVersion v ON v.ConnectorVersionId = e.ConnectorVersionId
    ORDER BY e.CreatedOn DESC;
END


Text
---------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------

/* ----------------------------------------------------------------------------
   APPROVE - freezes the approval hash over the exact reviewed package
   ---------------------------------------------------------------------------- */
CREATE PROCEDURE dbo
.API_Config_Execution_Approve
    @ExecutionId INT,
    @ActorName   NVARCHAR(128) = NULL,
    @Comment     NVARCHAR(1000) = NULL
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;

    DECLARE @approvalStatus NVARCHAR(20), @mode NVARCHAR(30), @approvalH
ash CHAR(64);

    SELECT @approvalStatus = ap.Status, @mode = e.ExecutionMode
    FROM dbo.API_Config_ExecutionApproval ap
    INNER JOIN dbo.API_Config_ExecutionRequest e ON e.ExecutionId = ap.ExecutionId
    WHERE ap.ExecutionId = @ExecutionId;

    IF
 @approvalStatus IS NULL
    BEGIN
        RAISERROR (N'API_Config: execution not found.', 16, 1);
        RETURN;
    END
    IF @approvalStatus <> N'Pending'
    BEGIN
        RAISERROR (N'API_Config: only a Pending approval can be approved.', 16, 1);
 
       RETURN;
    END

    BEGIN TRY
        BEGIN TRANSACTION;

        SELECT @approvalHash = CONVERT(CHAR(64), HASHBYTES('SHA2_256',
                   CONVERT(NVARCHAR(MAX), e.ExecutionId)    + N'|' +
                   CONVERT(NVARCHAR(MAX), e.Resol
vedMethod) + N'|' +
                   CONVERT(NVARCHAR(MAX), e.ResolvedUrl)    + N'|' +
                   ISNULL(e.ResolvedPayloadJson, N'')       + N'|' +
                   ISNULL(e.ConfigHash, N'')), 2)
        FROM dbo.API_Config_ExecutionRequest e 
WITH (UPDLOCK)
        WHERE e.ExecutionId = @ExecutionId;

        UPDATE dbo.API_Config_ExecutionApproval
           SET Status          = N'Approved',
               ApprovalHash    = @approvalHash,
               HashScheme      = N'PITSTOP-CANONICAL-
V1',
               DecidedOn    = GETDATE(),
               DecidedBy       = @ActorName,
               DecisionComment = @Comment
         WHERE ExecutionId = @ExecutionId AND Status = N'Pending';

        IF @@ROWCOUNT = 0
        BEGIN
            RA
ISERROR (N'API_Config: the approval was changed by another user. Reload and try again.', 16, 1);
            ROLLBACK TRANSACTION;
            RETURN;
        END

        /* Unattended-after-approval goes straight to the queue with an outbox row. */
    
    IF @mode = N'UnattendedAfterApproval'
        BEGIN
            UPDATE dbo.API_Config_ExecutionRequest
               SET Status = N'Queued'
             WHERE ExecutionId = @ExecutionId;

            INSERT INTO dbo.API_Config_OutboxMessage (Executio
nId, MessageType, PayloadJson)
            VALUES (@ExecutionId, N'ExecuteApprovedPackage',
                    N'{"executionId":' + CONVERT(NVARCHAR(20), @ExecutionId) + N'}');
        END
        ELSE
        BEGIN
            UPDATE dbo.API_Config_Exec
utionRequest
               SET Status = N'ApprovedAwaitingExecution'
             WHERE ExecutionId = @ExecutionId;
        END

        EXEC dbo.API_Config_Audit_Insert
            @EventType  = N'EXECUTION_APPROVED',
            @EntityType = N'Executi
onRequest',
            @EntityId   = @ExecutionId,
            @ActorName  = @ActorName;

        COMMIT TRANSACTION;
    END TRY
    BEGIN CATCH
        IF XACT_STATE() <> 0 ROLLBACK TRANSACTION;
        THROW;
    END CATCH

    SELECT ExecutionId = @E
xecutionId, ApprovalHash = @approvalHash;
END


Text
---------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------

/* Atomic claim - a duplicate transport message must not execute twice. */
CREATE PROCEDURE dbo.API_Config_Execution_Claim
    @ExecutionId INT,
    @Claimed     BIT OUTPUT
AS
BEGIN
    SET NOCOUNT ON;
    SET @Claimed = 0;

    UPDATE dbo.API_Config_Exe
cutionRequest
       SET Status = N'Executing'
     WHERE ExecutionId = @ExecutionId AND Status = N'Queued';

    IF @@ROWCOUNT = 1 SET @Claimed = 1;

    SELECT Claimed = @Claimed;
END


Text
---------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------

CREATE PROCEDURE dbo.API_Config_Execution_Complete
    @ExecutionId INT,
    @Status      NVARCHAR(40),
    @Reason      NVARCHAR(1000) = NULL
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;

    BEGIN TRANSACTION;

    UPDATE dbo.API_Config_Executio
nRequest
       SET Status = @Status, CompletedOn = GETDATE()
     WHERE ExecutionId = @ExecutionId;

    IF @Status IN (N'BusinessFailed', N'TechnicalFailed', N'UnknownOutcome')
        INSERT INTO dbo.API_Config_ExecutionException (ExecutionId, Category
, Reason)
        VALUES (@ExecutionId,
                CASE @Status
                    WHEN N'BusinessFailed'  THEN N'BusinessFailure'
                    WHEN N'UnknownOutcome'  THEN N'UnknownOutcome'
                    ELSE N'TechnicalFailure'
      
          END,
                ISNULL(@Reason, N'Execution did not succeed.'));

    EXEC dbo.API_Config_Audit_Insert
        @EventType  = N'EXECUTION_COMPLETED',
        @EntityType = N'ExecutionRequest',
        @EntityId   = @ExecutionId,
        @Det
ailsJson = @Status;

    COMMIT TRANSACTION;
END


Text
---------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------

/* ============================================================================
   HUMAN IN THE LOOP - create review
   ============================================================================ */
CREATE PROCEDURE dbo.API_Config_Execution_CreateReview

    @ConnectorVersionId  INT,
    @ExecutionUid        UNIQUEIDENTIFIER,
    @BusinessKey         NVARCHAR(200)  = NULL,
    @ExecutionMode       NVARCHAR(30),
    @ResolvedMethod      NVARCHAR(10),
    @ResolvedUrl         NVARCHAR(1000),
    @ResolvedP
ayloadJson NVARCHAR(MAX)  = NULL,
    @SourceDataJson      NVARCHAR(MAX)  = NULL,
    @ActorName           NVARCHAR(128)  = NULL,
    @ExecutionId         INT OUTPUT
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;

    DECLARE @connectorId INT, @confi
gHash CHAR(64), @status NVARCHAR(20),
            @modeAllowed NVARCHAR(30), @isActive BIT;

    SELECT @connectorId = v.ConnectorId,
           @configHash  = v.ConfigHash,
           @status      = v.Status,
           @modeAllowed = v.ExecutionModeAllo
wed,
           @isActive    = c.Enabled
    FROM dbo.API_Config_ConnectorVersion v
    INNER JOIN dbo.API_Config_Connector c ON c.ConnectorId = v.ConnectorId
    WHERE v.ConnectorVersionId = @ConnectorVersionId;

    IF @connectorId IS NULL
    BEGIN
   
     RAISERROR (N'API_Config: connector version not found.', 16, 1);
        RETURN;
    END
    IF @status <> N'Published' OR @isActive = 0
    BEGIN
        RAISERROR (N'API_Config: only an active, published connector version can create a review.', 16, 
1);
        RETURN;
    END
    IF (@modeAllowed = N'AttendedOnly' AND @ExecutionMode <> N'Attended')
       OR (@modeAllowed = N'UnattendedOnly' AND @ExecutionMode <> N'UnattendedAfterApproval')
    BEGIN
        RAISERROR (N'API_Config: the requested ex
ecution mode is not permitted by this connector version.', 16, 1);
        RETURN;
    END

    /* payload hash freezes exactly what the approver will see */
    DECLARE @payloadHash CHAR(64) = CONVERT(CHAR(64),
        HASHBYTES('SHA2_256',
            C
ONVERT(NVARCHAR(MAX), @ResolvedMethod) + N'|' +
            CONVERT(NVARCHAR(MAX), @ResolvedUrl)    + N'|' +
            ISNULL(@ResolvedPayloadJson, N'')), 2);

    BEGIN TRY
        BEGIN TRANSACTION;

        INSERT INTO dbo.API_Config_ExecutionRequest

            (ExecutionUid, ConnectorId, ConnectorVersionId, BusinessKey, ExecutionMode, Status,
             ResolvedMethod, ResolvedUrl, ResolvedPayloadJson, SourceDataJson, PayloadHash, ConfigHash, CreatedBy)
        VALUES
            (@ExecutionUid, 
@connectorId, @ConnectorVersionId, @BusinessKey, @ExecutionMode, N'PendingReview',
             @ResolvedMethod, @ResolvedUrl, @ResolvedPayloadJson, @SourceDataJson, @payloadHash, @configHash, @ActorName);

        SET @ExecutionId = CONVERT(INT, SCOPE_ID
ENTITY());

        INSERT INTO dbo.API_Config_ExecutionApproval (ExecutionId, Status)
        VALUES (@ExecutionId, N'Pending');

        EXEC dbo.API_Config_Audit_Insert
            @EventType  = N'EXECUTION_REVIEW_CREATED',
            @EntityType = N'
ExecutionRequest',
            @EntityId   = @ExecutionId,
            @ActorName  = @ActorName;

        COMMIT TRANSACTION;
    END TRY
    BEGIN CATCH
        IF XACT_STATE() <> 0 ROLLBACK TRANSACTION;
        THROW;
    END CATCH

    SELECT Execution
Id = @ExecutionId, ExecutionUid = @ExecutionUid,
           PayloadHash = @payloadHash, Status = N'PendingReview';
END


Text
---------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------

/* ============================================================================
   EXECUTION AUDIT SEARCH
   ============================================================================ */
CREATE PROCEDURE dbo.API_Config_Execution_GetAudit
    @FromDate 
DATETIME   = NULL,
    @ToDate   DATETIME   = NULL,
    @Status  NVARCHAR(40)   = NULL,
    @Search  NVARCHAR(200)  = NULL,
    @Top     INT            = 200
AS
BEGIN
    SET NOCOUNT ON;

    SELECT TOP (@Top)
        e.ExecutionId, e.ExecutionUid, e.Busi
nessKey, e.ExecutionMode, e.Status,
        e.ResolvedMethod, e.ResolvedUrl, e.PayloadHash, e.ConfigHash,
        e.CreatedOn, e.CompletedOn,
        c.ConnectorName, c.ConnectorCode, c.OperationCode,
        v.VersionNumber,
        cu.CustomerName, a.Ap
plicationName,
        ap.ApprovalId, ap.Status AS ApprovalStatus, ap.ApprovalHash, ap.DecidedBy, ap.DecidedOn,
        AttemptCount   = (SELECT COUNT(*) FROM dbo.API_Config_ExecutionAttempt t WHERE t.ExecutionId = e.ExecutionId),
        LastStatusCode =
 (SELECT TOP (1) t.HttpStatusCode FROM dbo.API_Config_ExecutionAttempt t
                          WHERE t.ExecutionId = e.ExecutionId ORDER BY t.AttemptNumber DESC)
    FROM dbo.API_Config_ExecutionRequest e
    INNER JOIN dbo.API_Config_ExecutionApprova
l ap ON ap.ExecutionId = e.ExecutionId
    INNER JOIN dbo.API_Config_Connector c          ON c.ConnectorId = e.ConnectorId
    INNER JOIN dbo.API_Config_ConnectorVersion v   ON v.ConnectorVersionId = e.ConnectorVersionId
    INNER JOIN dbo.API_Config_Cust
omer cu          ON cu.CustomerId = c.CustomerId
    INNER JOIN dbo.API_Config_Application a        ON a.ApplicationId = c.ApplicationId
    WHERE (@FromDate IS NULL OR e.CreatedOn >= @FromDate)
      AND (@ToDate   IS NULL OR e.CreatedOn <  @ToDate)
    
  AND (@Status  IS NULL OR @Status = N'' OR e.Status = @Status)
      AND (@Search  IS NULL OR @Search = N''
           OR e.BusinessKey LIKE N'%' + @Search + N'%'
           OR c.ConnectorName LIKE N'%' + @Search + N'%'
           OR CONVERT(NVARCHAR(50)
, e.ExecutionId) LIKE N'%' + @Search + N'%'
           OR CONVERT(NVARCHAR(50), e.ExecutionUid) LIKE N'%' + @Search + N'%')
    ORDER BY e.CreatedOn DESC;
END


Text
---------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------

CREATE PROCEDURE dbo.API_Config_Execution_GetById
    @ExecutionId INT
AS
BEGIN
    SET NOCOUNT ON;

    /* 0 - the frozen package */
    SELECT
        e.ExecutionId, e.ExecutionUid, e.BusinessKey, e.ExecutionMode, e.Status,
        e.ResolvedMethod, e.
ResolvedUrl, e.ResolvedPayloadJson, e.SourceDataJson,
        e.PayloadHash, e.ConfigHash, e.CreatedOn, e.CreatedBy, e.CompletedOn,
        c.ConnectorId, c.ConnectorName, c.ConnectorCode, c.OperationCode,
        v.ConnectorVersionId, v.VersionNumber, v.
TimeoutSeconds, v.MaxAttempts,
        v.IdempotencyHeader, v.CorrelationHeader, v.ResponseType, v.IsRetrySafe,
        v.PublishedSnapshotJson,
        cu.CustomerName, a.ApplicationName,
        ap.ApprovalId, ap.Status AS ApprovalStatus, ap.ApprovalHas
h, ap.HashScheme,
        ap.DecidedOn, ap.DecidedBy, ap.DecisionComment
    FROM dbo.API_Config_ExecutionRequest e
    INNER JOIN dbo.API_Config_ExecutionApproval ap ON ap.ExecutionId = e.ExecutionId
    INNER JOIN dbo.API_Config_Connector c          ON 
c.ConnectorId = e.ConnectorId
    INNER JOIN dbo.API_Config_ConnectorVersion v   ON v.ConnectorVersionId = e.ConnectorVersionId
    INNER JOIN dbo.API_Config_Customer cu          ON cu.CustomerId = c.CustomerId
    INNER JOIN dbo.API_Config_Application a 
       ON a.ApplicationId = c.ApplicationId
    WHERE e.ExecutionId = @ExecutionId;

    /* 1 - attempts */
    SELECT AttemptNumber, StartedOn, CompletedOn, HttpStatusCode, Outcome,
           DurationMs, ResponseSnippet, ErrorMessage
    FROM dbo.API_Co
nfig_ExecutionAttempt
    WHERE ExecutionId = @ExecutionId
    ORDER BY AttemptNumber;

    /* 2 - headers of the published version */
    SELECT h.HeaderName, h.HeaderValueTemplate
    FROM dbo.API_Config_Header h
    INNER JOIN dbo.API_Config_ExecutionR
equest e ON e.ConnectorVersionId = h.ConnectorVersionId
    AND h.Enabled = 1
    WHERE e.ExecutionId = @ExecutionId
    ORDER BY h.SortOrder;

    /* 3 - success codes */
    SELECT s.StatusCode
    FROM dbo.API_Config_SuccessCode s
    INNER JOIN dbo.AP
I_Config_ExecutionRequest e ON e.ConnectorVersionId = s.ConnectorVersionId
                                                AND s.Enabled = 1
    WHERE e.ExecutionId = @ExecutionId
    ORDER BY s.StatusCode;

    /* 4 - exceptions */
    SELECT ExceptionId
, Category, Reason, Status, CreatedOn, ResolvedOn, ResolvedBy
    FROM dbo.API_Config_ExecutionException
    WHERE ExecutionId = @ExecutionId
    ORDER BY CreatedOn DESC;
END


Text
---------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------

CREATE PROCEDURE dbo.API_Config_Execution_GetPending
AS
BEGIN
    SET NOCOUNT ON;
    SELECT
        e.ExecutionId, e.ExecutionUid, e.BusinessKey, e.ExecutionMode, e.Status,
        e.ResolvedMethod, e.ResolvedUrl, e.PayloadHash, e.CreatedOn,
        c.C
onnectorName, c.ConnectorCode, c.OperationCode,
        v.VersionNumber, v.ConfigHash,
        cu.CustomerName, a.ApplicationName,
        ap.ApprovalId, ap.Status AS ApprovalStatus
    FROM dbo.API_Config_ExecutionRequest e
    INNER JOIN dbo.API_Config_
ExecutionApproval ap ON ap.ExecutionId = e.ExecutionId
    INNER JOIN dbo.API_Config_Connector c          ON c.ConnectorId = e.ConnectorId
    INNER JOIN dbo.API_Config_ConnectorVersion v   ON v.ConnectorVersionId = e.ConnectorVersionId
    INNER JOIN dbo
.API_Config_Customer cu          ON cu.CustomerId = c.CustomerId
    INNER JOIN dbo.API_Config_Application a        ON a.ApplicationId = c.ApplicationId
    WHERE ap.Status = N'Pending'
    ORDER BY e.CreatedOn;
END


Text
---------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------

/* ----------------------------------------------------------------------------
   ATTENDED EXECUTE - status change and outbox row in one transaction
   ---------------------------------------------------------------------------- */
CREATE PROCEDURE dbo.
API_Config_Execution_Queue
    @ExecutionId INT,
    @ActorName   NVARCHAR(128) = NULL
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;

    DECLARE @approvalStatus NVARCHAR(20), @storedHash CHAR(64), @recomputed CHAR(64);

    SELECT @approvalStatus =
 ap.Status,
           @storedHash     = ap.ApprovalHash,
           @recomputed     = CONVERT(CHAR(64), HASHBYTES('SHA2_256',
                   CONVERT(NVARCHAR(MAX), e.ExecutionId)    + N'|' +
                   CONVERT(NVARCHAR(MAX), e.ResolvedMethod)
 + N'|' +
                   CONVERT(NVARCHAR(MAX), e.ResolvedUrl)    + N'|' +
                   ISNULL(e.ResolvedPayloadJson, N'')       + N'|' +
                   ISNULL(e.ConfigHash, N'')), 2)
    FROM dbo.API_Config_ExecutionApproval ap
    INNER JO
IN dbo.API_Config_ExecutionRequest e ON e.ExecutionId = ap.ExecutionId
    WHERE ap.ExecutionId = @ExecutionId;

    IF @approvalStatus <> N'Approved'
    BEGIN
        RAISERROR (N'API_Config: the execution is not approved.', 16, 1);
        RETURN;
    
END

    /* fail closed on tampering */
    IF @storedHash IS NULL OR @storedHash <> @recomputed
    BEGIN
        UPDATE dbo.API_Config_ExecutionApproval SET Status = N'Invalidated' WHERE ExecutionId = @ExecutionId;
        UPDATE dbo.API_Config_Executio
nRequest  SET Status = N'ApprovalInvalidated' WHERE ExecutionId = @ExecutionId;

        INSERT INTO dbo.API_Config_ExecutionException (ExecutionId, Category, Reason)
        VALUES (@ExecutionId, N'ControlFailure',
                N'Approval hash mismatc
h. The package changed after approval, so execution was blocked.');

        EXEC dbo.API_Config_Audit_Insert
            @EventType  = N'EXECUTION_HASH_MISMATCH',
            @EntityType = N'ExecutionRequest',
            @EntityId   = @ExecutionId,
    
        @ActorName  = @ActorName;

        RAISERROR (N'API_Config: approval hash mismatch. Execution blocked and approval invalidated.', 16, 1);
        RETURN;
    END

    BEGIN TRY
        BEGIN TRANSACTION;

        UPDATE dbo.API_Config_ExecutionReq
uest
           SET Status = N'Queued'
         WHERE ExecutionId = @ExecutionId AND Status = N'ApprovedAwaitingExecution';

        IF @@ROWCOUNT = 0
        BEGIN
            RAISERROR (N'API_Config: the execution is not awaiting execution.', 16, 1);
  
          ROLLBACK TRANSACTION;
            RETURN;
        END

        INSERT INTO dbo.API_Config_OutboxMessage (ExecutionId, MessageType, PayloadJson)
        VALUES (@ExecutionId, N'ExecuteApprovedPackage',
                N'{"executionId":' + CONVERT
(NVARCHAR(20), @ExecutionId) + N'}');

        EXEC dbo.API_Config_Audit_Insert
            @EventType  = N'EXECUTION_QUEUED',
            @EntityType = N'ExecutionRequest',
            @EntityId   = @ExecutionId,
            @ActorName  = @ActorName;

  
      COMMIT TRANSACTION;
    END TRY
    BEGIN CATCH
        IF XACT_STATE() <> 0 ROLLBACK TRANSACTION;
        THROW;
    END CATCH
END


Text
---------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------

CREATE PROCEDURE dbo.API_Config_Execution_RecordAttempt
    @ExecutionId     INT,
    @HttpStatusCode  INT            = NULL,
    @Outcome         NVARCHAR(40),
    @DurationMs      INT            = NULL,
    @ResponseSnippet NVARCHAR(4000) = NULL,
    @
ErrorMessage    NVARCHAR(2000) = NULL
AS
BEGIN
    SET NOCOUNT ON;
    DECLARE @attemptNo INT;

    SELECT @attemptNo = ISNULL(MAX(AttemptNumber), 0) + 1
    FROM dbo.API_Config_ExecutionAttempt WHERE ExecutionId = @ExecutionId;

    INSERT INTO dbo.API_C
onfig_ExecutionAttempt
        (ExecutionId, AttemptNumber, CompletedOn, HttpStatusCode, Outcome, DurationMs, ResponseSnippet, ErrorMessage)
    VALUES
        (@ExecutionId, @attemptNo, GETDATE(), @HttpStatusCode, @Outcome, @DurationMs, @ResponseSnippet,
 @ErrorMessage);

    SELECT AttemptNumber = @attemptNo;
END


Text
---------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------

CREATE PROCEDURE dbo.API_Config_Execution_Reject
    @ExecutionId INT,
    @ActorName   NVARCHAR(128)  = NULL,
    @Comment     NVARCHAR(1000) = NULL
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;

    BEGIN TRY
        BEGIN TRANSACTION;

        U
PDATE dbo.API_Config_ExecutionApproval
           SET Status          = N'Rejected',
               DecidedOn    = GETDATE(),
               DecidedBy       = @ActorName,
               DecisionComment = @Comment
         WHERE ExecutionId = @ExecutionId 
AND Status = N'Pending';

        IF @@ROWCOUNT = 0
        BEGIN
            RAISERROR (N'API_Config: only a Pending approval can be rejected.', 16, 1);
            ROLLBACK TRANSACTION;
            RETURN;
        END

        UPDATE dbo.API_Config_Exec
utionRequest
           SET Status = N'Rejected', CompletedOn = GETDATE()
         WHERE ExecutionId = @ExecutionId;

        EXEC dbo.API_Config_Audit_Insert
            @EventType  = N'EXECUTION_REJECTED',
            @EntityType = N'ExecutionRequest',

            @EntityId   = @ExecutionId,
            @ActorName  = @ActorName;

        COMMIT TRANSACTION;
    END TRY
    BEGIN CATCH
        IF XACT_STATE() <> 0 ROLLBACK TRANSACTION;
        THROW;
    END CATCH
END


Text
---------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------

/* ----------------------------------------------------------------------------
   DEV/TEST ONLY - simulate a post-approval data change so the hash check fires
   ---------------------------------------------------------------------------- */
CREATE PROC
EDURE dbo.API_Config_Execution_SimulateTamper
    @ExecutionId INT
AS
BEGIN
    SET NOCOUNT ON;
    UPDATE dbo.API_Config_ExecutionRequest
       SET ResolvedPayloadJson = JSON_MODIFY(ISNULL(ResolvedPayloadJson, N'{}'),
                                   
          N'$.tamperedOn',
                                             CONVERT(NVARCHAR(40), GETDATE(), 126))
     WHERE ExecutionId = @ExecutionId;

    EXEC dbo.API_Config_Audit_Insert
        @EventType  = N'EXECUTION_SIMULATED_DATA_CHANGE',
        @
EntityType = N'ExecutionRequest',
        @EntityId   = @ExecutionId;
END


Text
---------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------

/* ----------------------------------------------------------------------------
   WORKER SIDE
   ---------------------------------------------------------------------------- */
CREATE PROCEDURE dbo.API_Config_Outbox_GetPending
    @Top INT = 10
AS
BEGIN

    SET NOCOUNT ON;
    SELECT TOP (@Top) OutboxId, ExecutionId, MessageType, PayloadJson, Attempts, CreatedOn
    FROM dbo.API_Config_OutboxMessage WITH (READPAST)
    WHERE Status = N'Pending'
    ORDER BY OutboxId;
END


Text
---------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------

CREATE PROCEDURE dbo.API_Config_Outbox_MarkPublished
    @OutboxId  INT,
    @Succeeded BIT,
    @Error     NVARCHAR(2000) = NULL
AS
BEGIN
    SET NOCOUNT ON;
    UPDATE dbo.API_Config_OutboxMessage
       SET Status         = CASE WHEN @Succeeded = 1 TH
EN N'Published' ELSE N'Failed' END,
           PublishedOn = CASE WHEN @Succeeded = 1 THEN GETDATE() ELSE NULL END,
           Attempts       = Attempts + 1,
           LastError      = @Error
     WHERE OutboxId = @OutboxId;
END


Text
---------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------

CREATE PROCEDURE dbo.API_Config_Provider_GetList
AS
BEGIN
    SET NOCOUNT ON;
    SELECT ProviderId, ProviderCategory, ProviderAlias, AdapterType, SettingsJson, Enabled
    FROM dbo.API_Config_Provider
    ORDER BY ProviderCategory, ProviderAlias;
END



Completion time: 2026-09-18T10:25:06.8533890+00:00
