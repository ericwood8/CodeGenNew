CREATE OR ALTER PROCEDURE [dbo].[LoadTest_Self_Load]
-- Loads the 2 row(s) of [dbo].[LoadTest_Self] that existed when this was generated. Safe to run repeatedly: a row
-- whose primary key is already present is left alone. Load tables that this one refers to first.
AS
BEGIN
	SET NOCOUNT ON;
	SET XACT_ABORT ON;

	-- Join the caller's transaction if there is one; otherwise own this one.
	DECLARE @blnOwnsTransaction BIT = CASE WHEN @@TRANCOUNT = 0 THEN 1 ELSE 0 END;

	BEGIN TRY
		IF @blnOwnsTransaction = 1
			BEGIN TRANSACTION;

		SET IDENTITY_INSERT [dbo].[LoadTest_Self] ON;

		IF NOT EXISTS (SELECT 1 FROM [dbo].[LoadTest_Self] WHERE [ID] = 10)
			INSERT INTO [dbo].[LoadTest_Self]([ID], [ParentID], [Name])
			  VALUES (10, NULL, 'root');

		IF NOT EXISTS (SELECT 1 FROM [dbo].[LoadTest_Self] WHERE [ID] = 20)
			INSERT INTO [dbo].[LoadTest_Self]([ID], [ParentID], [Name])
			  VALUES (20, 10, 'child');

		SET IDENTITY_INSERT [dbo].[LoadTest_Self] OFF;

		IF @blnOwnsTransaction = 1
			COMMIT TRANSACTION;

		RETURN 0;
	END TRY
	BEGIN CATCH
		SET IDENTITY_INSERT [dbo].[LoadTest_Self] OFF;
		IF @blnOwnsTransaction = 1 AND @@TRANCOUNT > 0
			ROLLBACK TRANSACTION;
		THROW;
	END CATCH;
END
