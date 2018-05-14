﻿#region Related components
using System;
using System.Linq;
using System.Web;
using System.Threading;
using System.Threading.Tasks;
using System.Collections;
using System.Collections.Generic;
using System.Collections.Concurrent;
using System.Collections.Specialized;
using System.Diagnostics;

using Microsoft.AspNetCore.Http;

using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

using net.vieapps.Components.Utility;
using net.vieapps.Components.Security;
using net.vieapps.Components.Repository;
#endregion

namespace net.vieapps.Services
{
	public static partial class Global
	{
		/// <summary>
		/// Calls a business service
		/// </summary>
		/// <param name="context"></param>
		/// <param name="requestInfo">The requesting information</param>
		/// <param name="cancellationToken">The cancellation token</param>
		/// <param name="onStart">The action to run when start</param>
		/// <param name="onSuccess">The action to run when success</param>
		/// <param name="onError">The action to run when got an error</param>
		/// <returns></returns>
		public static async Task<JObject> CallServiceAsync(this HttpContext context, RequestInfo requestInfo, CancellationToken cancellationToken = default(CancellationToken), Action<RequestInfo> onStart = null, Action<RequestInfo, JObject> onSuccess = null, Action<RequestInfo, Exception> onError = null)
		{
			// get the instance of service
			IService service = null;
			try
			{
				service = await WAMPConnections.GetServiceAsync(requestInfo.ServiceName).ConfigureAwait(false);
			}
			catch (Exception ex)
			{
				onError?.Invoke(requestInfo, ex);
				throw new ServiceNotFoundException($"The service \"{requestInfo.ServiceName}\" is not found", ex);
			}

			// call the service
			var stopwatch = Stopwatch.StartNew();
			try
			{
				onStart?.Invoke(requestInfo);
				if (Global.IsDebugResultsEnabled)
					await context.WriteLogsAsync($"<REST> Begin request of service ({requestInfo.Verb} /{requestInfo.ServiceName?.ToLower()}/{requestInfo.ObjectName?.ToLower()}/{requestInfo.GetObjectIdentity()?.ToLower()}) - {requestInfo.Session.AppName} ({requestInfo.Session.AppPlatform}) @ {requestInfo.Session.IP}");

				var json = await service.ProcessRequestAsync(requestInfo, cancellationToken).ConfigureAwait(false);

				onSuccess?.Invoke(requestInfo, json);
				if (Global.IsDebugResultsEnabled)
					await context.WriteLogsAsync(new List<string>
					{
						$"<REST> Request:\r\n{requestInfo.ToJson().ToString(Global.IsDebugLogEnabled ? Formatting.Indented : Formatting.None)}",
						$"<REST> Response:\r\n{json?.ToString(Global.IsDebugLogEnabled ? Formatting.Indented : Formatting.None)}"
					}).ConfigureAwait(false);

				// TO DO: track counter of success

				return json;
			}
			catch (WampSharp.V2.Client.WampSessionNotEstablishedException)
			{
				await Task.Delay(567, cancellationToken).ConfigureAwait(false);
				try
				{
					var json = await service.ProcessRequestAsync(requestInfo, cancellationToken).ConfigureAwait(false);

					onSuccess?.Invoke(requestInfo, json);
					if (Global.IsDebugResultsEnabled)
						await context.WriteLogsAsync(new List<string>
						{
							$"<REST> Request (re-call):\r\n{requestInfo.ToJson().ToString(Global.IsDebugLogEnabled ? Formatting.Indented : Formatting.None)}",
							$"<REST> Response:\r\n{json?.ToString(Global.IsDebugLogEnabled ? Formatting.Indented : Formatting.None)}"
						}).ConfigureAwait(false);

					// TO DO: track counter of success

					return json;
				}
				catch (Exception)
				{
					throw;
				}
			}
			catch (Exception ex)
			{
				// TO DO: track counter of error

				onError?.Invoke(requestInfo, ex);

				throw ex;
			}
			finally
			{
				stopwatch.Stop();
				// TO DO: track counter of average times

				if (Global.IsDebugResultsEnabled)
					await context.WriteLogsAsync($"<REST> End request of service ({requestInfo.Verb} /{requestInfo.ServiceName?.ToLower()}/{requestInfo.ObjectName?.ToLower()}/{requestInfo.GetObjectIdentity()?.ToLower()}) - {requestInfo.Session.AppName} ({requestInfo.Session.AppPlatform}) @ {requestInfo.Session.IP} - Execution times: {stopwatch.GetElapsedTimes()}").ConfigureAwait(false);
			}
		}

		/// <summary>
		/// Calls a business service
		/// </summary>
		/// <param name="requestInfo">The requesting information</param>
		/// <param name="cancellationToken">The cancellation token</param>
		/// <param name="onStart">The action to run when start</param>
		/// <param name="onSuccess">The action to run when success</param>
		/// <param name="onError">The action to run when got an error</param>
		/// <returns></returns>
		public static Task<JObject> CallServiceAsync(RequestInfo requestInfo, CancellationToken cancellationToken = default(CancellationToken), Action<RequestInfo> onStart = null, Action<RequestInfo, JObject> onSuccess = null, Action<RequestInfo, Exception> onError = null)
			=> Global.CurrentHttpContext.CallServiceAsync(requestInfo, cancellationToken, onStart, onSuccess, onError);

		/// <summary>
		/// Calls a business service
		/// </summary>
		/// <param name="context"></param>
		/// <param name="serviceName"></param>
		/// <param name="objectName"></param>
		/// <param name="verb"></param>
		/// <param name="query"></param>
		/// <param name="extra"></param>
		/// <param name="onStart"></param>
		/// <param name="onSuccess"></param>
		/// <param name="onError"></param>
		/// <returns></returns>
		public static Task<JObject> CallServiceAsync(this HttpContext context, string serviceName, string objectName, string verb, Dictionary<string, string> query, Dictionary<string, string> extra = null, Action<RequestInfo> onStart = null, Action<RequestInfo, JObject> onSuccess = null, Action<RequestInfo, Exception> onError = null)
			=> context.CallServiceAsync(new RequestInfo(context.GetSession(UtilityService.NewUUID, context.User?.Identity as UserIdentity), serviceName, objectName, verb, query, null, null, extra, context.GetCorrelationID()), Global.CancellationTokenSource.Token, onStart, onSuccess, onError);

		/// <summary>
		/// Calls a business service
		/// </summary>
		/// <param name="serviceName"></param>
		/// <param name="objectName"></param>
		/// <param name="verb"></param>
		/// <param name="query"></param>
		/// <param name="extra"></param>
		/// <param name="onStart"></param>
		/// <param name="onSuccess"></param>
		/// <param name="onError"></param>
		/// <returns></returns>
		public static Task<JObject> CallServiceAsync(string serviceName, string objectName, string verb, Dictionary<string, string> query, Dictionary<string, string> extra = null, Action<RequestInfo> onStart = null, Action<RequestInfo, JObject> onSuccess = null, Action<RequestInfo, Exception> onError = null)
			=> Global.CurrentHttpContext.CallServiceAsync(serviceName, objectName, verb, query, extra, onStart, onSuccess, onError);

		internal static ILoggingService _LoggingService = null;

		/// <summary>
		/// Gets the logging service
		/// </summary>
		public static ILoggingService LoggingService
		{
			get
			{
				if (Global._LoggingService == null)
					Task.WaitAll(new[] { Global.InitializeLoggingServiceAsync() }, TimeSpan.FromSeconds(3));
				return Global._LoggingService;
			}
		}

		/// <summary>
		/// Initializes the logging service
		/// </summary>
		/// <returns></returns>
		public static async Task InitializeLoggingServiceAsync()
		{
			if (Global._LoggingService == null)
			{
				await WAMPConnections.OpenOutgoingChannelAsync().ConfigureAwait(false);
				Global._LoggingService = WAMPConnections.OutgoingChannel.RealmProxy.Services.GetCalleeProxy<ILoggingService>(ProxyInterceptor.Create());
			}
		}

		internal static IRTUService _RTUService = null;

		/// <summary>
		/// Gets the RTU service
		/// </summary>
		public static IRTUService RTUService
		{
			get
			{
				if (Global._RTUService == null)
					Task.WaitAll(new[] { Global.InitializeRTUServiceAsync() }, TimeSpan.FromSeconds(3));
				return Global._RTUService;
			}
		}

		/// <summary>
		/// Initializes the real-time updater (RTU) service
		/// </summary>
		/// <returns></returns>
		public static async Task InitializeRTUServiceAsync()
		{
			if (Global._RTUService == null)
			{
				await WAMPConnections.OpenOutgoingChannelAsync().ConfigureAwait(false);
				Global._RTUService = WAMPConnections.OutgoingChannel.RealmProxy.Services.GetCalleeProxy<IRTUService>(ProxyInterceptor.Create());
			}
		}
	}
}