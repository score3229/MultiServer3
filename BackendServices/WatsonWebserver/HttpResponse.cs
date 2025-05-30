﻿namespace WatsonWebserver
{
    using System;
    using System.Collections.Generic;
    using System.Collections.Specialized;
    using System.IO;
    using System.Linq;
    using System.Text;
    using System.Threading;
    using System.Threading.Tasks;
    using System.Text.Json.Serialization;
    using WatsonWebserver.Core;
    using NetworkLibrary.Extension;
    using SpaceWizards.HttpListener;

    /// <summary>
    /// HTTP response.
    /// </summary>
    public class HttpResponse : HttpResponseBase
    {
        #region Public-Members

        /// <summary>
        /// Retrieve the response body sent using a Send() or SendAsync() method.
        /// </summary>
        [JsonIgnore]
        public override string DataAsString
        {
            get
            {
                if (_DataAsBytes != null) return Encoding.UTF8.GetString(_DataAsBytes);
                if (_Data != null && ContentLength > 0)
                {
                    _DataAsBytes = ReadStreamFully(_Data);
                    if (_DataAsBytes != null) return Encoding.UTF8.GetString(_DataAsBytes);
                }
                return null;
            }
        }

        /// <summary>
        /// Retrieve the response body sent using a Send() or SendAsync() method.
        /// </summary>
        [JsonIgnore]
        public override byte[] DataAsBytes
        {
            get
            {
                if (_DataAsBytes != null) return _DataAsBytes;
                if (_Data != null && ContentLength > 0)
                {
                    _DataAsBytes = ReadStreamFully(_Data);
                    return _DataAsBytes;
                }
                return null;
            }
        }

        /// <summary>
        /// Response data stream sent to the requestor.
        /// </summary>
        [JsonIgnore]
        public override MemoryStream Data
        {
            get
            {
                if (_Data == null)
                    throw new ArgumentNullException(nameof(_Data), "Input stream cannot be null");

                else if (!_Data.CanRead)
                    throw new NotSupportedException("Input stream is not readable");

                else if (!_Data.CanSeek)
                    throw new NotSupportedException("Input stream is not seekable");

                else if (_Data is MemoryStream data)
                    return data;

                MemoryStream ms = new MemoryStream();

                if (ContentLength <= 0)
                    return ms;

                long dataPos = _Data.Position;

                try
                {
                    _Data.CopyTo(ms);
                }
                catch (Exception e)
                {
                    CustomLogger.LoggerAccessor.LogError($"[WatsonWebserver] - Data: an exception was thrown while copying data to the MemmoryStream: {e}");
                    ms.Clear();
                }

                _Data.Position = dataPos;

                ms.Seek(0, SeekOrigin.Begin);
                return ms;
            }
        }

        #endregion

        #region Private-Members

        internal HttpListenerResponse _Response = null;
        internal Stream _OutputStream = null;

        private HttpRequestBase _Request = null;
        private HttpListenerContext _Context = null;
        private bool _HeadersSet = false;
        private bool _KeepAliveData = true;

        private WebserverSettings _Settings = new WebserverSettings();
        private WebserverEvents _Events = new WebserverEvents();

        private NameValueCollection _Headers = new NameValueCollection(StringComparer.InvariantCultureIgnoreCase);
        private byte[] _DataAsBytes = null;
        private Stream _Data = null;
        private ISerializationHelper _Serializer = null;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate the object.
        /// </summary>
        public HttpResponse()
        {

        }

        internal HttpResponse(
            HttpRequestBase req, 
            HttpListenerContext ctx, 
            WebserverSettings settings, 
            WebserverEvents events,
            ISerializationHelper serializer,
            bool KeepAliveResponseData)
        {
            if (req == null) throw new ArgumentNullException(nameof(req));
            if (ctx == null) throw new ArgumentNullException(nameof(ctx));
            if (settings == null) throw new ArgumentNullException(nameof(settings));
            if (events == null) throw new ArgumentNullException(nameof(events));
            if (serializer == null) throw new ArgumentNullException(nameof(serializer));

            _Serializer = serializer;
            _Request = req;
            _Context = ctx;
            _Response = _Context.Response;
            _Settings = settings;
            _Events = events;
            _KeepAliveData = KeepAliveResponseData;

            _OutputStream = _Response.OutputStream;
        }

        #endregion

        #region Public-Methods
         
        /// <inheritdoc />
        public override async Task<bool> Send(CancellationToken token = default)
        {
            if (ChunkedTransfer) throw new IOException("Response is configured to use chunked transfer-encoding.  Use SendChunk() and SendFinalChunk().");
            return await SendInternalAsync(0, null, token).ConfigureAwait(false);
        }

        /// <inheritdoc />
        public override async Task<bool> Send(long contentLength, CancellationToken token = default)
        {
            if (ChunkedTransfer) throw new IOException("Response is configured to use chunked transfer-encoding.  Use SendChunk() and SendFinalChunk().");
            ContentLength = contentLength;
            return await SendInternalAsync(0, null, token).ConfigureAwait(false);
        }

        /// <inheritdoc />
        public override async Task<bool> Send(string data, CancellationToken token = default)
        {
            if (ChunkedTransfer) throw new IOException("Response is configured to use chunked transfer-encoding.  Use SendChunk() and SendFinalChunk().");
            if (String.IsNullOrEmpty(data))
                return await SendInternalAsync(0, null, token).ConfigureAwait(false);

            byte[] bytes = Encoding.UTF8.GetBytes(data);
            using (var ms = new MemoryStream(bytes))
                return await SendInternalAsync(bytes.Length, ms, token).ConfigureAwait(false);
        }

        /// <inheritdoc />
        public override async Task<bool> Send(byte[] data, CancellationToken token = default)
        {
            if (ChunkedTransfer) throw new IOException("Response is configured to use chunked transfer-encoding.  Use SendChunk() and SendFinalChunk().");
            if (data == null || data.Length < 1)
                    return await SendInternalAsync(0, null, token).ConfigureAwait(false);

            using (var ms = new MemoryStream(data))
                return await SendInternalAsync(data.Length, ms, token).ConfigureAwait(false);
        }

        /// <inheritdoc />
        public override async Task<bool> Send(long contentLength, Stream stream, CancellationToken token = default)
        {
            if (ChunkedTransfer) throw new IOException("Response is configured to use chunked transfer-encoding.  Use SendChunk() and SendFinalChunk().");
            if (stream == null || !stream.CanRead)
                return await SendInternalAsync(0, null, token).ConfigureAwait(false);

            return await SendInternalAsync(contentLength, stream, token).ConfigureAwait(false);
        }

        /// <inheritdoc />
        public override async Task<bool> SendChunk(byte[] chunk, bool isFinal, CancellationToken token = default)
        {
            if (!ChunkedTransfer) throw new IOException("Response is not configured to use chunked transfer-encoding.  Set ChunkedTransfer to true first, otherwise use Send().");
            if (!_HeadersSet) SendHeaders();

            if (chunk != null && chunk.Length > 0)
                ContentLength += chunk.Length;

            try
            {
                if (chunk == null || chunk.Length < 1) chunk = Array.Empty<byte>();
                await _OutputStream.WriteAsync(chunk, 0, chunk.Length, token).ConfigureAwait(false);
                await _OutputStream.FlushAsync(token).ConfigureAwait(false);

                if (isFinal)
                {
                    byte[] endChunk = Array.Empty<byte>();
                    await _OutputStream.WriteAsync(endChunk, 0, endChunk.Length, token).ConfigureAwait(false);
                    await _OutputStream.FlushAsync(token).ConfigureAwait(false);

                    _OutputStream.Close();

                    if (_Response != null) _Response.Close();
                    ResponseSent = true;
                }
            }
            catch (Exception)
            {
                return false;
            }

            return true;
        }

        /// <inheritdoc />
        public override async Task<bool> SendEvent(string eventData, bool isFinal, CancellationToken token = default)
        {
            if (!ServerSentEvents) throw new IOException("Response is not configured to use server-sent events.  Set ServerSentEvents to true first, otherwise use Send().");
            if (!_HeadersSet) SendHeaders();

            if (!String.IsNullOrEmpty(eventData))
                ContentLength += eventData.Length;

            try
            {
                if (String.IsNullOrEmpty(eventData)) eventData = string.Empty;

                byte[] dataBytes = Encoding.UTF8.GetBytes("data: " + eventData + "\n\n");
                await _OutputStream.WriteAsync(dataBytes, 0, dataBytes.Length, token).ConfigureAwait(false);
                await _OutputStream.FlushAsync(token).ConfigureAwait(false);

                if (isFinal)
                {
                    byte[] endChunk = Array.Empty<byte>();
                    await _OutputStream.WriteAsync(endChunk, 0, endChunk.Length, token).ConfigureAwait(false);
                    await _OutputStream.FlushAsync(token).ConfigureAwait(false);

                    _OutputStream.Close();

                    if (_Response != null) _Response.Close();
                    ResponseSent = true;
                }

                return true;
            }
            catch (Exception)
            {
                return false;
            }
        }

        #endregion

        #region Private-Methods

        private string GetStatusDescription(int statusCode)
        {
            //
            // Helpful links:
            // https://developer.mozilla.org/en-US/docs/Web/HTTP/Status
            // https://en.wikipedia.org/wiki/List_of_HTTP_status_codes
            // 

            switch (statusCode)
            {
                case 100:
                    return "Continue";
                case 101:
                    return "Switching Protocols";
                case 102:
                    return "Processing";
                case 103:
                    return "Early Hints";

                case 200:
                    return "OK";
                case 201:
                    return "Created";
                case 202:
                    return "Accepted";
                case 203:
                    return "Non-Authoritative Information";
                case 204:
                    return "No Content";
                case 205:
                    return "Reset Content";
                case 206:
                    return "Partial Content";
                case 207:
                    return "Multi-Status";
                case 208:
                    return "Already Reported";
                case 226:
                    return "IM Used";

                case 300:
                    return "Multiple Choices";
                case 301:
                    return "Moved Permanently";
                case 302:
                    return "Moved Temporarily";
                case 303:
                    return "See Other";
                case 304:
                    return "Not Modified";
                case 305:
                    return "Use Proxy";
                case 306:
                    return "Switch Proxy";
                case 307:
                    return "Temporary Redirect";
                case 308:
                    return "Permanent Redirect";

                case 400:
                    return "Bad Request";
                case 401:
                    return "Unauthorized";
                case 402:
                    return "Payment Required";
                case 403:
                    return "Forbidden";
                case 404:
                    return "Not Found";
                case 405:
                    return "Method Not Allowed";
                case 406:
                    return "Not Acceptable";
                case 407:
                    return "Proxy Authentication Required";
                case 408:
                    return "Request Timeout";
                case 409:
                    return "Conflict";
                case 410:
                    return "Gone";
                case 411:
                    return "Length Required";
                case 412:
                    return "Precondition Failed";
                case 413:
                    return "Payload too Large";
                case 414:
                    return "URI Too Long";
                case 415:
                    return "Unsupported Media Type";
                case 416:
                    return "Range Not Satisfiable";
                case 417:
                    return "Expectation Failed";
                case 418:
                    return "I'm a teapot";
                case 421:
                    return "Misdirected Request";
                case 422:
                    return "Unprocessable Content";
                case 423:
                    return "Locked";
                case 424:
                    return "Failed Dependency";
                case 425:
                    return "Too Early";
                case 426:
                    return "Upgrade Required";
                case 428:
                    return "Precondition Required";
                case 429:
                    return "Too Many Requests";
                case 431:
                    return "Request Header Fields Too Large";
                case 451:
                    return "Unavailable For Legal Reasons";

                case 500:
                    return "Internal Server Error";
                case 501:
                    return "Not Implemented";
                case 502:
                    return "Bad Gateway";
                case 503:
                    return "Service Unavailable";
                case 504:
                    return "Gateway Timeout";
                case 505:
                    return "HTTP Version Not Supported";
                case 506:
                    return "Variant Also Negotiates";
                case 507:
                    return "Insufficient Storage";
                case 508:
                    return "Loop Detected";
                case 510:
                    return "Not Extended";
                case 511:
                    return "Network Authentication Required";
            }

            return "Unknown";
        }

        private void SendHeaders()
        {
            if (_HeadersSet) throw new IOException("Headers already sent.");

            _Response.ProtocolVersion = new Version(1, 1);
            _Response.ContentLength64 = ContentLength;
            _Response.StatusCode = StatusCode;
            _Response.StatusDescription = GetStatusDescription(StatusCode);
            _Response.SendChunked = (ChunkedTransfer || ServerSentEvents);
            _Response.ContentType = ContentType;
            _Response.KeepAlive = false;

            if (ServerSentEvents)
            {
                _Response.ContentType = "text/event-stream; charset=utf-8";
                _Response.Headers.Add("Cache-Control", "no-cache");
                _Response.Headers.Add("Connection", "keep-alive");
            }

            if (Headers != null && Headers.Count > 0)
            {
                for (int i = 0; i < Headers.Count; i++)
                {
                    string key = Headers.GetKey(i);
                    string[] vals = Headers.GetValues(i);

                    if (vals == null || vals.Length < 1)
                    {
                        _Response.AddHeader(key, null);
                    }
                    else
                    {
                        for (int j = 0; j < vals.Length; j++)
                        {
                            _Response.AddHeader(key, vals[j]);
                        }
                    }
                }
            }

            if (_Settings.Headers.DefaultHeaders != null && _Settings.Headers.DefaultHeaders.Count > 0)
            {
                foreach (KeyValuePair<string, string> header in _Settings.Headers.DefaultHeaders)
                {
                    if (Headers.Get(header.Key) != null || Headers.AllKeys.Contains(header.Key))
                    {
                        // already present
                    }
                    else
                    {
                        _Response.AddHeader(header.Key, header.Value);
                    }
                }
            }

            _HeadersSet = true;
        }

        private byte[] ReadStreamFully(Stream input)
        {
            if (input == null) throw new ArgumentNullException(nameof(input));
            if (!input.CanRead) throw new InvalidOperationException("Input stream is not readable");

            byte[] buffer = new byte[16 * 1024];
            using (MemoryStream ms = new MemoryStream())
            {
                int read;

                while ((read = input.Read(buffer, 0, buffer.Length)) > 0)
                {
                    ms.Write(buffer, 0, read);
                }

                return ms.ToArray();
            }
        }

        private async Task<bool> SendInternalAsync(long contentLength, Stream stream, CancellationToken token = default)
        {
            if (ChunkedTransfer) throw new IOException("Response is configured to use chunked transfer-encoding.  Use SendChunk() and SendFinalChunk().");

            if (ContentLength == 0 && contentLength > 0) ContentLength = contentLength;

            if (!_HeadersSet) SendHeaders();

            try
            {
                if (_Request.Method != HttpMethod.HEAD)
                {
                    if (stream != null && stream.CanRead)
                    {
                        // We override the bufferSize for large content, else, we murder the CPU.
                        int bufferSize = ContentLength > 8000000 && _Settings.IO.StreamBufferSize < 500000 ? 500000 : _Settings.IO.StreamBufferSize;

                        // Some clients might cut the connection while the data is being copied, this is expected, so we simply ignore failed writes.
                        if (ContentLength > 0)
                        {
                            if (_KeepAliveData)
                            {
                                int bytesRead;
                                long bytesRemaining = contentLength;

                                byte[] buffer = new byte[bufferSize];

                                _Data = new MemoryStream();

                                while (bytesRemaining > 0)
                                {
                                    bytesRead = await stream.ReadAsync(buffer, 0, buffer.Length, token).ConfigureAwait(false);
                                    if (bytesRead > 0)
                                    {
                                        await _Data.WriteAsync(buffer, 0, bytesRead, token).ConfigureAwait(false);
                                        await _OutputStream.WriteAsync(buffer, 0, bytesRead, token).ConfigureAwait(false);
                                        bytesRemaining -= bytesRead;
                                    }
                                }

                                _Data.Seek(0, SeekOrigin.Begin);
                            }
                            else
                                await StreamUtils.CopyStreamAsync(stream, _OutputStream, bufferSize, ContentLength, false, token).ConfigureAwait(false);
                        }
                        else
                            await StreamUtils.CopyStreamAsync(stream, _OutputStream, bufferSize, false, token).ConfigureAwait(false);

                        await _OutputStream.FlushAsync(token).ConfigureAwait(false);
                    }
                }

                _OutputStream.Close();

                if (_Response != null) _Response.Close();

                ResponseSent = true;
                return true;
            }
            catch
            {
                return false;
            }
        }

        #endregion
    }
}
