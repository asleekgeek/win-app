﻿/*
 * Copyright (c) 2024 Proton AG
 *
 * This file is part of ProtonVPN.
 *
 * ProtonVPN is free software: you can redistribute it and/or modify
 * it under the terms of the GNU General Public License as published by
 * the Free Software Foundation, either version 3 of the License, or
 * (at your option) any later version.
 *
 * ProtonVPN is distributed in the hope that it will be useful,
 * but WITHOUT ANY WARRANTY; without even the implied warranty of
 * MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
 * GNU General Public License for more details.
 *
 * You should have received a copy of the GNU General Public License
 * along with ProtonVPN.  If not, see <https://www.gnu.org/licenses/>.
 */

using System.Net;
using System.Net.Http.Headers;
using ProtonVPN.Core.Settings;
using ProtonVPN.IssueReporting.Contracts;
using ProtonVPN.Logging.Contracts;
using ProtonVPN.Logging.Contracts.Events.ProcessCommunicationLogs;
using ProtonVPN.ProcessCommunication.Common;

namespace ProtonVPN.ProcessCommunication.Client;

public class ResponseHandler : DelegatingHandler
{
    private readonly SemaphoreSlim _semaphore = new(1, 1);
    private readonly TimeSpan _minimumRestartInterval = TimeSpan.FromMinutes(2);

    private readonly ILogger _logger;
    private readonly IIssueReporter _issueReporter;
    private readonly IAppSettings _appSettings;
    private readonly EventHandler _invokingAppRestart;

    private readonly bool _isToHandle;
    private bool _isAppRestartInvoked;
    private bool _is401UnauthorizedWithoutBeingBelowServiceVersionHandled;

    public ResponseHandler(ILogger logger, IIssueReporter issueReporter, IAppSettings appSettings, EventHandler invokingAppRestart, HttpMessageHandler innerHandler)
        : base(innerHandler)
    {
        _logger = logger;
        _issueReporter = issueReporter;
        _appSettings = appSettings;
        _invokingAppRestart = invokingAppRestart;

        _isToHandle = logger is not null && issueReporter is not null && appSettings is not null;
    }

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        HttpResponseMessage response = await base.SendAsync(request, cancellationToken);
        await HandleResponseAsync(response);
        return response;
    }

    private async Task HandleResponseAsync(HttpResponseMessage response)
    {
        if (_isToHandle && !response.IsSuccessStatusCode)
        {
            string clientProcessPath = GetHeaderValue(response.Headers, HttpConfiguration.CLIENT_PROCESS_PATH);
            string serviceProcessPath = GetHeaderValue(response.Headers, HttpConfiguration.SERVICE_PROCESS_PATH);
            string installedServicePath = GetHeaderValue(response.Headers, HttpConfiguration.INSTALLED_SERVICE_PATH);

            string clientProcessVersion = GetHeaderValue(response.Headers, HttpConfiguration.CLIENT_PROCESS_VERSION);
            string serviceProcessVersion = GetHeaderValue(response.Headers, HttpConfiguration.SERVICE_PROCESS_VERSION);
            string installedServiceVersion = GetHeaderValue(response.Headers, HttpConfiguration.INSTALLED_SERVICE_VERSION);

            _logger.Error<ProcessCommunicationErrorLog>(
                $"Received HTTP status code {response.StatusCode} from gRPC server. " +
                $"Client Process Path: '{clientProcessPath}' Version '{clientProcessVersion}'), " +
                $"Service Process Path: '{serviceProcessPath}' Version '{serviceProcessVersion}'), " +
                $"Installed Service Path: '{installedServicePath}' Version '{installedServiceVersion}')");

            if (response.StatusCode == HttpStatusCode.Unauthorized)
            {
                await Handle401UnauthorizedAsync(clientProcessVersion, serviceProcessVersion);
            }
        }
    }

    private async Task Handle401UnauthorizedAsync(string clientProcessVersion, string serviceProcessVersion)
    {
        await _semaphore.WaitAsync();
        try
        {
            Handle401Unauthorized(clientProcessVersion, serviceProcessVersion);
        }
        finally
        {
            _semaphore.Release();
        }
    }

    private void Handle401Unauthorized(string clientProcessVersionString, string serviceProcessVersionString)
    {
        string versions = $"ClientProcessVersion: {clientProcessVersionString}, ServiceProcessVersion: {serviceProcessVersionString}";

        if (Version.TryParse(clientProcessVersionString, out Version clientProcessVersion) &&
            Version.TryParse(serviceProcessVersionString, out Version serviceProcessVersion) &&
            clientProcessVersion < serviceProcessVersion)
        {
            HandleClientVersionLowerThanServiceVersion(versions);
        }
        else
        {
            const string logExplanation = "Received 401 Unauthorized from gRPC server but " +
                "the client process version is not below the service process version.";
            _logger.Warn<ProcessCommunicationErrorLog>($"{logExplanation} {versions}");

            if (!_is401UnauthorizedWithoutBeingBelowServiceVersionHandled)
            {
                _issueReporter.CaptureMessage(logExplanation, versions);
                _is401UnauthorizedWithoutBeingBelowServiceVersionHandled = true;
            }
        }
    }

    private void HandleClientVersionLowerThanServiceVersion(string versions)
    {
        if (_isAppRestartInvoked)
        {
            _logger.Warn<ProcessCommunicationErrorLog>($"Ignoring 401 Unauthorized because client restart was already invoked. {versions}");
            return;
        }

        if (_appSettings.LastProcessVersionMismatchRestartVersions == versions &&
            _appSettings.LastProcessVersionMismatchRestartUtcDate is not null &&
            (_appSettings.LastProcessVersionMismatchRestartUtcDate + _minimumRestartInterval) > DateTimeOffset.UtcNow)
        {
            string logMessage = $"Cannot restart the client because that was done for " +
                $"the current version pair less than {_minimumRestartInterval} ago " +
                $"(Last restart date: {_appSettings.LastProcessVersionMismatchRestartUtcDate}). {versions}";
            _logger.Error<ProcessCommunicationErrorLog>(logMessage);
            return;
        }

        const string logExplanation = "Restarting the client because the version is inferior to the service process version.";
        _logger.Warn<ProcessCommunicationErrorLog>($"{logExplanation} {versions}");
        _issueReporter.CaptureMessage(logExplanation, versions);

        _appSettings.LastProcessVersionMismatchRestartVersions = versions;
        _appSettings.LastProcessVersionMismatchRestartUtcDate = DateTimeOffset.UtcNow;
        _invokingAppRestart?.Invoke(this, EventArgs.Empty);
        _isAppRestartInvoked = true;
    }

    private string GetHeaderValue(HttpResponseHeaders headers, string headerKey)
    {
        string value = null;
        if (headers.TryGetValues(headerKey, out IEnumerable<string> values))
        {
            value = values.FirstOrDefault();
        }
        return value;
    }
}