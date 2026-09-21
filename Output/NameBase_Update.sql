CREATE OR ALTER PROCEDURE [dbo].[NameBase_Update]
(
@plngID VarChar(20) = NULL,
@pstrSortName VarChar(60) = NULL,
@pstrName VarChar(60) = NULL,
@pstrNumber VarChar(50) = NULL,
@pblnIs1099Required VarChar(1) = NULL,
@pblnIsInactive VarChar(1) = NULL,
@pblnIsCustomer VarChar(1) = NULL,
@plngNameBaseCustomerID VarChar(20) = NULL,
@pblnIsEmployee VarChar(1) = NULL,
@plngNameBaseEmployeeID VarChar(20) = NULL,
@pblnIsOwner VarChar(1) = NULL,
@plngOwnerID VarChar(20) = NULL,
@pblnIsVendor VarChar(1) = NULL,
@plngVendorOptionsID VarChar(20) = NULL,
@pblnIsBank VarChar(1) = NULL,
@plngNameBaseBankID VarChar(20) = NULL,
@pblnIsCompany VarChar(1) = NULL,
@plngCompanyID VarChar(20) = NULL,
@plngSYIRSEntityCodeID VarChar(20) = NULL,
@plngRegularPayTypeEnumID VarChar(20) = NULL,
@plngCheckStubDetailEnumID VarChar(20) = NULL,
@pblnIsPrintable VarChar(1) = NULL,
@pbinEncryptedFederalIdNumber VarChar(20) = NULL,
@pstrWebAddress VarChar(50) = NULL,
@pstrCreateUser VarChar(50) = NULL,
@pdteInactiveDate VarChar(40) = NULL,
@pstrNoteText VarChar(20) = NULL,
@pstrInactiveReasonNoteText VarChar(20) = NULL
)
AS
	DECLARE @strUpdate varchar(8000);
	DECLARE @strSetStatement varchar(8000);

	DECLARE @lngReturn int;
	SET @strSetStatement = '';
	SET @strSetStatement = @strSetStatement + ' [LastDateChanged] = GETDATE(),';
	SET @strSetStatement = @strSetStatement + ' [ModifiedDate] = GETDATE(),';

	IF @plngID IS NOT NULL
	BEGIN
	    SET @strUpdate = 'UPDATE [NameBase] SET ';
	    IF @pstrSortName IS NOT NULL
	        SET @strSetStatement = @strSetStatement + ' [SortName] = ''' + REPLACE(RTRIM(LTRIM(@pstrSortName)), '''', '''''') + ''',';
	    IF @pstrName IS NOT NULL
	        SET @strSetStatement = @strSetStatement + ' [Name] = ''' + REPLACE(RTRIM(LTRIM(@pstrName)), '''', '''''') + ''',';
	    IF @pstrNumber IS NOT NULL
	        SET @strSetStatement = @strSetStatement + ' [Number] = ''' + REPLACE(RTRIM(LTRIM(@pstrNumber)), '''', '''''') + ''',';
	    IF @pblnIs1099Required IS NOT NULL
	        SET @strSetStatement = @strSetStatement + ' [Is1099Required] = ''' + @pblnIs1099Required + ''',';
	    IF @pblnIsCustomer IS NOT NULL
	        SET @strSetStatement = @strSetStatement + ' [IsCustomer] = ''' + @pblnIsCustomer + ''',';
	    IF @plngNameBaseCustomerID IS NOT NULL
	        SET @strSetStatement = @strSetStatement + ' [NameBaseCustomerID] = ''' + @plngNameBaseCustomerID + ''',';
	    IF @pblnIsEmployee IS NOT NULL
	        SET @strSetStatement = @strSetStatement + ' [IsEmployee] = ''' + @pblnIsEmployee + ''',';
	    IF @plngNameBaseEmployeeID IS NOT NULL
	        SET @strSetStatement = @strSetStatement + ' [NameBaseEmployeeID] = ''' + @plngNameBaseEmployeeID + ''',';
	    IF @pblnIsOwner IS NOT NULL
	        SET @strSetStatement = @strSetStatement + ' [IsOwner] = ''' + @pblnIsOwner + ''',';
	    IF @plngOwnerID IS NOT NULL
	        SET @strSetStatement = @strSetStatement + ' [OwnerID] = ''' + @plngOwnerID + ''',';
	    IF @pblnIsVendor IS NOT NULL
	        SET @strSetStatement = @strSetStatement + ' [IsVendor] = ''' + @pblnIsVendor + ''',';
	    IF @plngVendorOptionsID IS NOT NULL
	        SET @strSetStatement = @strSetStatement + ' [VendorOptionsID] = ''' + @plngVendorOptionsID + ''',';
	    IF @pblnIsBank IS NOT NULL
	        SET @strSetStatement = @strSetStatement + ' [IsBank] = ''' + @pblnIsBank + ''',';
	    IF @plngNameBaseBankID IS NOT NULL
	        SET @strSetStatement = @strSetStatement + ' [NameBaseBankID] = ''' + @plngNameBaseBankID + ''',';
	    IF @pblnIsCompany IS NOT NULL
	        SET @strSetStatement = @strSetStatement + ' [IsCompany] = ''' + @pblnIsCompany + ''',';
	    IF @plngCompanyID IS NOT NULL
	        SET @strSetStatement = @strSetStatement + ' [CompanyID] = ''' + @plngCompanyID + ''',';
	    IF @plngSYIRSEntityCodeID IS NOT NULL
	        SET @strSetStatement = @strSetStatement + ' [SYIRSEntityCodeID] = ''' + @plngSYIRSEntityCodeID + ''',';
	    IF @plngRegularPayTypeEnumID IS NOT NULL
	        SET @strSetStatement = @strSetStatement + ' [RegularPayTypeEnumID] = ''' + @plngRegularPayTypeEnumID + ''',';
	    IF @plngCheckStubDetailEnumID IS NOT NULL
	        SET @strSetStatement = @strSetStatement + ' [CheckStubDetailEnumID] = ''' + @plngCheckStubDetailEnumID + ''',';
	    IF @pblnIsPrintable IS NOT NULL
	        SET @strSetStatement = @strSetStatement + ' [IsPrintable] = ''' + @pblnIsPrintable + ''',';
	    IF @pbinEncryptedFederalIdNumber IS NOT NULL
	        SET @strSetStatement = @strSetStatement + ' [EncryptedFederalIdNumber] = ''' + @pbinEncryptedFederalIdNumber + ''',';
	    IF @pstrWebAddress IS NOT NULL
	        SET @strSetStatement = @strSetStatement + ' [WebAddress] = ''' + REPLACE(RTRIM(LTRIM(@pstrWebAddress)), '''', '''''') + ''',';
	    IF @pstrCreateUser IS NOT NULL
	        SET @strSetStatement = @strSetStatement + ' [CreateUser] = ''' + REPLACE(RTRIM(LTRIM(@pstrCreateUser)), '''', '''''') + ''',';
	    IF @pdteInactiveDate IS NOT NULL
	        SET @strSetStatement = @strSetStatement + ' [InactiveDate] = ''' + @pdteInactiveDate + ''',';
	    IF @pstrNoteText IS NOT NULL
	        SET @strSetStatement = @strSetStatement + ' [NoteText] = ''' + REPLACE(RTRIM(LTRIM(@pstrNoteText)), '''', '''''') + ''',';
	    IF @pstrInactiveReasonNoteText IS NOT NULL
	        SET @strSetStatement = @strSetStatement + ' [InactiveReasonNoteText] = ''' + REPLACE(RTRIM(LTRIM(@pstrInactiveReasonNoteText)), '''', '''''') + ''',';
	    IF @pdteInactiveDate IS NOT NULL OR @pstrInactiveReasonNoteText IS NOT NULL
	        SET @strSetStatement = @strSetStatement + ' [IsInactive] = ''1'',';
	    ELSE IF @pblnIsInactive IS NOT NULL
	        SET @strSetStatement = @strSetStatement + ' [IsInactive] = ''' + @pblnIsInactive + ''',';
	    IF LEN(@strSetStatement) > 0
	    BEGIN
	        IF RIGHT(@strSetStatement, 1) = ','
	            SET @strSetStatement = LEFT(@strSetStatement, LEN(@strSetStatement) - 1);

	        SET @strUpdate = @strUpdate + @strSetStatement + ' WHERE ([ID] = ' + @plngID + ')';
	        EXEC (@strUpdate);

	        IF @@ERROR = 0
	            SELECT @lngReturn = 1;
	        ELSE
	            SELECT @lngReturn = 0;
	    END
	END
RETURN IsNull(@lngReturn, 0);
