CREATE OR ALTER PROCEDURE [dbo].[NameBase_Delete]
(
@plngID int
)
AS
BEGIN
	DECLARE @lngReturn INT;

	DELETE FROM [dbo].[NameBase] WHERE ([ID] = @plngID);

	DECLARE @lngDeleteError INT = @@ERROR;
	IF @lngDeleteError = 547
		SELECT @lngReturn = -1;
	ELSE IF @lngDeleteError <> 0
		SELECT @lngReturn = -2;
	ELSE
		SELECT @lngReturn = 0;

	RETURN IsNull(@lngReturn, 0);
END
