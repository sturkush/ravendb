using System.IO;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;

#if SILVERLIGHT
using Ionic.Zlib;
#else
using System.IO.Compression;
#endif

namespace Raven.Client.Connection
{
	public class CompressedStringContent : HttpContent
	{
		private readonly string data;
		private readonly bool disableRequestCompression;

		public CompressedStringContent(string data, bool disableRequestCompression)
		{
			this.data = data;
			this.disableRequestCompression = disableRequestCompression;

			if (disableRequestCompression == false)
			{
				Headers.ContentEncoding.Add("gzip");
			}
		}

		protected override async Task SerializeToStreamAsync(Stream stream, TransportContext context)
		{
			if (disableRequestCompression == false)
				stream = new GZipStream(stream, CompressionMode.Compress, true);

			using (var streamWriter = new StreamWriter(stream, Encoding.UTF8, 4096
#if !SILVERLIGHT
				, true
#endif
				))
			{
				await streamWriter.WriteAsync(data);
				await streamWriter.FlushAsync();
			}

			if (disableRequestCompression == false)
				stream.Dispose();
		}

		protected override bool TryComputeLength(out long length)
		{
			if (disableRequestCompression)
			{
				length = data.Length;
				return true;
			}

			length = -1;
			return false;
		}
	}
}