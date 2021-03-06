using System;
using System.Linq;
using ServiceStack.Serialization;
using ServiceStack.Templates;
using ServiceStack.Web;

namespace ServiceStack.Formats
{
    public class HtmlFormat : IPlugin
    {
        public static string TitleFormat
            = @"{0} Snapshot of {1}";

        public static string HtmlTitleFormat
            = @"Snapshot of <i>{0}</i> generated by <a href=""https://servicestack.net"">ServiceStack</a> on <b>{1}</b>";

        public static bool Humanize = true;

        private IAppHost AppHost { get; set; }

        public const string ModelKey = "Model";
        public const string ErrorStatusKey = "__errorStatus";

        public void Register(IAppHost appHost)
        {
            AppHost = appHost;
            //Register this in ServiceStack with the custom formats
            appHost.ContentTypes.Register(MimeTypes.Html, SerializeToStream, null);
            appHost.ContentTypes.Register(MimeTypes.JsonReport, SerializeToStream, null);

            appHost.Config.DefaultContentType = MimeTypes.Html;
            appHost.Config.IgnoreFormatsInMetadata.Add(MimeTypes.Html.ToContentFormat());
            appHost.Config.IgnoreFormatsInMetadata.Add(MimeTypes.JsonReport.ToContentFormat());
        }

        public void SerializeToStream(IRequest request, object response, IResponse httpRes)
        {
            var httpResult = request.GetItem("HttpResult") as IHttpResult;
            if (httpResult != null && httpResult.Headers.ContainsKey(HttpHeaders.Location) 
                && httpResult.StatusCode != System.Net.HttpStatusCode.Created)  
                return;

            try
            {
                if (httpRes.StatusCode >= 400)
                {
                    var responseStatus = response.GetResponseStatus();
                    request.Items[ErrorStatusKey] = responseStatus;
                }

                if (AppHost.ViewEngines.Any(x => x.ProcessRequest(request, httpRes, response))) return;
            }
            catch (Exception ex)
            {
                if (httpRes.StatusCode < 400)
                    throw;

                //If there was an exception trying to render a Error with a View, 
                //It can't handle errors so just write it out here.
                response = DtoUtils.CreateErrorResponse(request.Dto, ex);
            }

            if (request.ResponseContentType != MimeTypes.Html
                && request.ResponseContentType != MimeTypes.JsonReport) return;

            var dto = response.GetDto();
            var html = dto as string;
            if (html == null)
            {
                // Serialize then escape any potential script tags to avoid XSS when displaying as HTML
                var json = JsonDataContractSerializer.Instance.SerializeToString(dto) ?? "null";
                json = json.Replace("<", "&lt;").Replace(">", "&gt;");

                var url = request.AbsoluteUri
                    .Replace("format=html", "")
                    .Replace("format=shtm", "")
                    .TrimEnd('?', '&');

                url += url.Contains("?") ? "&" : "?";

                var now = DateTime.UtcNow;
                var requestName = request.OperationName ?? dto.GetType().GetOperationName();

                html = HtmlTemplates.GetHtmlFormatTemplate()
                    .Replace("${Dto}", json)
                    .Replace("${Title}", string.Format(TitleFormat, requestName, now))
                    .Replace("${MvcIncludes}", MiniProfiler.Profiler.RenderIncludes().ToString())
                    .Replace("${Header}", string.Format(HtmlTitleFormat, requestName, now))
                    .Replace("${ServiceUrl}", url)
                    .Replace("${Humanize}", Humanize.ToString().ToLower());
            }

            var utf8Bytes = html.ToUtf8Bytes();
            httpRes.OutputStream.Write(utf8Bytes, 0, utf8Bytes.Length);
        }
    }

}