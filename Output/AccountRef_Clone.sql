CREATE OR ALTER PROCEDURE [dbo].[AccountRef_Clone]
-- Copies the AccountRef row named by @CopyFrom... into a new row and returns the new key.
(
@CopyFromAccountRefID uniqueidentifier,
@NewAccountRefID uniqueidentifier = NULL OUTPUT
)
AS
BEGIN
	SET NOCOUNT ON;
	SET XACT_ABORT ON;

	SET @NewAccountRefID = NEWID(); -- a new key for the new row
	-- Join the caller's transaction if there is one; otherwise own this one.
	DECLARE @blnOwnsTransaction BIT = CASE WHEN @@TRANCOUNT = 0 THEN 1 ELSE 0 END;

	BEGIN TRY
		IF @blnOwnsTransaction = 1
			BEGIN TRANSACTION;

		IF NOT EXISTS (SELECT 1 FROM [dbo].[AccountRef] AS [src] WITH (UPDLOCK, HOLDLOCK) WHERE [src].[AccountRefID] = @CopyFromAccountRefID)
		BEGIN
			DECLARE @ErrorMessage VARCHAR(400);
			SET @ErrorMessage = 'No AccountRef found to clone (' + 'AccountRefID = ' + ISNULL(CONVERT(VARCHAR(100), @CopyFromAccountRefID), 'NULL') + ').';
			THROW 55509, @ErrorMessage, 1;
		END

		INSERT INTO [dbo].[AccountRef]
		(
			[ListID],
			[FullName],
			[AccountRefID]
		)
		SELECT
			[src].[ListID],
			[src].[FullName],
			@NewAccountRefID
		FROM [dbo].[AccountRef] AS [src]
		WHERE [src].[AccountRefID] = @CopyFromAccountRefID;

		IF @blnOwnsTransaction = 1
			COMMIT TRANSACTION;

		RETURN 0;
	END TRY
	BEGIN CATCH
		IF @blnOwnsTransaction = 1 AND @@TRANCOUNT > 0
			ROLLBACK TRANSACTION;
		THROW;
	END CATCH;
END
