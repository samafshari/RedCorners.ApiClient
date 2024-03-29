﻿using System;
using System.Linq;
using RestSharp;
using Newtonsoft.Json;
using System.Net;
using System.Threading.Tasks;
using RedCorners;
using System.Diagnostics;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Collections;

namespace RedCorners
{
    public static class ApiClientExtensions
    {
        public static bool IsOk(this IRestResponse response) =>
            response != null && response.StatusCode == HttpStatusCode.OK;
    }

    public abstract class ApiClient
    {
        public event EventHandler<string> OnLog;
        public event EventHandler<IRestResponse> OnServerError;
        public event EventHandler<IRestResponse> OnResponse;
        public event EventHandler<IRestResponse> OnSuccess;
        public event EventHandler<IRestResponse> OnGone;

        protected virtual void Log(string message, [CallerMemberName] string method = null)
        {
            OnLog?.Invoke(method, message);
        }

        public virtual string BaseUrl { get; set; }
        public virtual Func<Dictionary<string, string>> AdditionalHeaders { get; set; }
        
        public ApiClient() { }

        public virtual async Task<IRestResponse> RequestAsync(string path, Method method = Method.GET, object dto = null, Action<RestRequest> buildRequest = null, string contentType = null)
        {
            try
            {
                Log($"Request: [/{path}] {method} {path}");
                RestClient client = CreateClient(BaseUrl);
                var request = CreateRequest(path, method);
                buildRequest?.Invoke(request);
                if (dto != null)
                {
                    if (method == Method.GET)
                    {
                        var json = JsonConvert.SerializeObject(dto);
                        var dic = JsonConvert.DeserializeObject<Dictionary<string, object>>(json);
                        foreach (var pair in dic)
                        {
                            if (pair.Value != null)
                            {
                                if (!(pair.Value is string) && pair.Value is IEnumerable e)
                                {
                                    foreach (var item in e)
                                    {
                                        request.AddQueryParameter(pair.Key, item.ToString());
                                    }
                                }
                                else
                                {
                                    request.AddQueryParameter(pair.Key, pair.Value.ToString());
                                }
                            }
                        }
                    }
                    else
                    {
                        request.AddJsonBody(dto);
                    }
                }
                contentType = contentType ?? "application/json";
                request.AddHeader("Content-Type", contentType);
                request.AddHeader("Accept", contentType);
                Stopwatch watch = new Stopwatch();
                watch.Start();
                try
                {
                    var result = await client.ExecuteAsync(request);
                    OnResponse?.Invoke(this, result);
                    Log($"[{watch.Elapsed.TotalSeconds}s/{BaseUrl}] {request.Method} {result?.ResponseUri?.ToString() ?? path} response ({result.StatusCode}): {result.Content.Head(1024)}\n");
                    if (result.StatusCode == HttpStatusCode.Gone)
                    {
                        OnGone?.Invoke(this, result);
                        return null;
                    }
                    if (result.StatusCode != HttpStatusCode.OK)
                        OnServerError?.Invoke(this, result);
                    OnSuccess?.Invoke(this, result);
                    return result;
                }
                catch (Exception ex)
                {
                    Log($"Exception while executing task: {ex}");
                }
                finally
                {
                    watch.Stop();
                }
            }
            catch (Exception ex)
            {
                Log($"Exception while executing task: {ex}");
            }
            return null;
        }

        public virtual async Task<T> RequestAsync<T>(string path, Method method = Method.GET, object dto = null, Action<RestRequest> buildRequest = null, Action onfail = null, string contentType = null)
        {
            var response = await RequestAsync(path, method, dto, buildRequest, contentType);
            return DeserializeResponse<T>(response, onfail);
        }

        public virtual T DeserializeResponse<T>(IRestResponse response, Action onfail = null)
        {
            if (response.IsOk())
            {
                try
                {
                    return JsonConvert.DeserializeObject<T>(response.Content);
                }
                catch (Exception ex)
                {
                    Log($"RequestAsync deserialization failed: {ex}");
                }
            }
            if (response != null && response.StatusCode != (HttpStatusCode)400)
                onfail?.Invoke();
            return default;
        }

        protected virtual RestClient CreateClient(string url) => new RestClient(url);
        protected virtual RestRequest CreateRequest(string path, Method method)
        {
            var request = new RestRequest(path, method);

            if (AdditionalHeaders != null)
                foreach (var item in AdditionalHeaders())
                    request.AddHeader(item.Key, item.Value);

            return request;
        }
    }
}
