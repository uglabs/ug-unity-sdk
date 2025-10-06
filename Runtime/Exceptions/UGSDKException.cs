using System;

namespace UG.Exceptions
{
    public class UGSDKException : Exception
    {
        public UGSDKException(string message) : base(message)
        {
        }
    }
}