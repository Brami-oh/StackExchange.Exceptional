﻿using Newtonsoft.Json;
using StackExchange.Exceptional.Internal;
using StackExchange.Exceptional.Pages;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Web;

namespace StackExchange.Exceptional
{
    /// <summary>
    /// Single handler for all module requests, async style.
    /// </summary>
    internal class ExceptionalAsyncHandler : HttpTaskAsyncHandler
    {
        private static readonly JsonSerializer serializer = new JsonSerializer();
        private string Url { get; }
        public ExceptionalAsyncHandler(string url) => Url = url;

        public override async Task ProcessRequestAsync(HttpContext context)
        {
            void JsonResult(bool result)
            {
                context.Response.ContentType = "text/javascript";
                context.Response.Write($@"{{""result"":{(result ? "true" : "false")}}}");
            }
            void Content(string content, string mime = "text/html")
            {
                context.Response.ContentType = mime;
                context.Response.Write(content);
            }
            void Page(WebPage page) => Content(page.Render());
            void Resource(Resources.ResourceCache cache) => Content(cache.Content, cache.MimeType);

            // In MVC requests, PathInfo isn't set - determine via Path..
            // e.g. "/admin/errors/info" or "/admin/errors/"
            var match = Regex.Match(context.Request.Path, @"/?(?<resource>[\w\-\.]+)/?$");
            var resource = match.Success ? match.Groups["resource"].Value.ToLower(CultureInfo.InvariantCulture) : string.Empty;

            Func<IEnumerable<Guid>> getFormGuids = () =>
            {
                var idsStr = context.Request.Form["ids"];
                try { if (idsStr.HasValue()) return idsStr.Split(',').Select(Guid.Parse); }
                catch { return Enumerable.Empty<Guid>(); }
                return Enumerable.Empty<Guid>();
            };

            string errorGuid;

            switch (context.Request.HttpMethod)
            {
                case "POST":
                    errorGuid = context.Request.Form["guid"] ?? string.Empty;
                    switch (resource)
                    {
                        case KnownRoutes.Delete:
                            JsonResult(await ErrorStore.Default.DeleteAsync(errorGuid.ToGuid()).ConfigureAwait(false));
                            return;
                        case KnownRoutes.DeleteAll:
                            JsonResult(await ErrorStore.Default.DeleteAllAsync().ConfigureAwait(false));
                            return;
                        case KnownRoutes.DeleteList:
                            JsonResult(await ErrorStore.Default.DeleteAsync(getFormGuids()).ConfigureAwait(false));
                            return;
                        case KnownRoutes.Protect:
                            JsonResult(await ErrorStore.Default.ProtectAsync(errorGuid.ToGuid()).ConfigureAwait(false));
                            return;
                        case KnownRoutes.ProtectList:
                            JsonResult(await ErrorStore.Default.ProtectAsync(getFormGuids()).ConfigureAwait(false));
                            return;
                        default:
                            Content("Invalid POST Request");
                            return;
                    }
                case "GET":
                    errorGuid = context.Request.QueryString["guid"] ?? string.Empty;
                    switch (resource)
                    {
                        case KnownRoutes.Info:
                            var guid = errorGuid.ToGuid();
                            var error = errorGuid.HasValue() ? ErrorStore.Default.Get(guid) : null;
                            Page(new ErrorDetailPage(error, ErrorStore.Default, TrimEnd(context.Request.Path, "/info"), guid));
                            return;
                        case KnownRoutes.Json:
                            context.Response.ContentType = "application/json";
                            DateTime? since = long.TryParse(context.Request["since"], out long sinceLong)
                                     ? new DateTime(1970, 1, 1, 0, 0, 0).AddSeconds(sinceLong)
                                     : (DateTime?)null;

                            var errors = ErrorStore.Default.GetAll();
                            if (since.HasValue)
                            {
                                errors = errors.Where(e => e.CreationDate >= since).ToList();
                            }
                            serializer.Serialize(context.Response.Output, errors);
                            return;
                        case KnownRoutes.Css:
                            Resource(Resources.BundleCss);
                            return;
                        case KnownRoutes.Js:
                            Resource(Resources.BundleJs);
                            return;
                        case KnownRoutes.Test:
                            throw new Exception("This is a test. Please disregard. If this were a real emergency, it'd have a different message.");

                        default:
                            context.Response.Cache.SetCacheability(HttpCacheability.NoCache);
                            context.Response.Cache.SetNoStore();
                            Page(new ErrorListPage(ErrorStore.Default, Url));
                            return;
                    }
                default:
                    Content("Unsupported request method: " + context.Request.HttpMethod);
                    return;
            }
        }

        private string TrimEnd(string s, string value) =>
            s.EndsWith(value) ? s.Remove(s.LastIndexOf(value, StringComparison.Ordinal)) : s;
    }
}
