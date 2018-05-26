using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq.Expressions;
using System.Reflection;
using System.Text;

namespace Hangfire.Logging
{
    public class ElmahCoreLogProvider : ILogProvider
    {
        private static bool _providerIsAvailableOverride = true;

        private const LogLevel DefaultMinLevel = LogLevel.Error;
        private readonly Type _errorType;

        private readonly LogLevel _minLevel;
        private readonly Func<object> _getErrorLogDelegate;

        public ElmahCoreLogProvider()
            : this(DefaultMinLevel)
        {
        }

        public ElmahCoreLogProvider(LogLevel minLevel)
        {
            if (!IsLoggerAvailable())
            {
                throw new InvalidOperationException("`Elmah.ErrorLog` or `Elmah.Error` type not found");
            }

            _minLevel = minLevel;

            _errorType = GetErrorType();
            _getErrorLogDelegate = GetGetErrorLogMethodCall();
        }

        public static bool ProviderIsAvailableOverride
        {
            get { return _providerIsAvailableOverride; }
            set { _providerIsAvailableOverride = value; }
        }

        public ILog GetLogger(string name)
        {
            return new ElmahLog(_minLevel, _getErrorLogDelegate(), _errorType);
        }

        public static bool IsLoggerAvailable()
        {
            return ProviderIsAvailableOverride && GetLogManagerType() != null && GetErrorType() != null;
        }

        private static Type GetLogManagerType()
        {
            return Type.GetType("Elmah.ErrorLog, Elmah");
        }

        private static Type GetHttpContextType()
        {
            return Type.GetType(
                $"System.Web.HttpContext, System.Web, Version={Environment.Version}, Culture=neutral, PublicKeyToken=b03f5f7f11d50a3a");
        }

        private static Type GetErrorType()
        {
            return Type.GetType("Elmah.Error, Elmah");
        }

        private static Func<object> GetGetErrorLogMethodCall()
        {
            Type logManagerType = GetLogManagerType();
            Type httpContextType = GetHttpContextType();
            MethodInfo method = logManagerType.GetMethod("GetDefault", new[] { httpContextType });
            ConstantExpression contextValue = Expression.Constant(null, httpContextType);
            MethodCallExpression methodCall = Expression.Call(null, method, new Expression[] { contextValue });
            return Expression.Lambda<Func<object>>(methodCall).Compile();
        }

        internal class ElmahLog : ILog
        {
            private readonly LogLevel _minLevel;
            private readonly Type _errorType;
            private readonly dynamic _errorLog;

            public ElmahLog(LogLevel minLevel, dynamic errorLog, Type errorType)
            {
                _minLevel = minLevel;
                _errorType = errorType;
                _errorLog = errorLog;
            }

            public bool Log(LogLevel logLevel, Func<string> messageFunc, Exception exception)
            {
                if (messageFunc == null) return logLevel >= _minLevel;

                var message = messageFunc();

                dynamic error = exception == null
                    ? Activator.CreateInstance(_errorType)
                    : Activator.CreateInstance(_errorType, exception);

                error.Message = message;
                error.Type = logLevel.ToString();
                error.Time = DateTime.Now;
                error.ApplicationName = "Hangfire";

                try
                {
                    _errorLog.Log(error);
                }
                catch (Exception ex)
                {
                    Debug.Print("Error: {0}\n{1}", ex.Message, ex.StackTrace);
                }

                return true;
            }
        }
    }
}
