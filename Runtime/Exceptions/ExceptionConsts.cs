namespace UG.Exceptions
{
    public class ExceptionConsts
    {
        public const string NotInitialized = "[UGSDK] SDK is not initialized. Please make sure you call UGSDK.Initialize() first";
        public const string AlreadyInitialized = "[UGSDK] SDK is already initialized.";
        public const string SettingsValidationFailed = "[UGSDK] Init error - settings is missing. In Unity. Click on Tools -> UG Labs -> Settings -> Create Settings";
        public const string SettingsInvalidHost = "[UGSDK] Init error - settings is missing Host. In Unity. Click on Tools -> UG Labs -> Settings -> Open Settings and set Host";
        public const string AudioConversionException = "[UGSDK] Audio conversion error - make sure byte array provided is in PCM Wave format";
        public const string InvalidContextType = "[UGSDK] This context data type is not supported";
        public const string NotAuthenticated = "[UGSDK] Authentication failed. Please check if your credentials are correct";
    }
}
