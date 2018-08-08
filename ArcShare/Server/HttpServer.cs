﻿using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Windows.Storage.Streams;
using Windows.Networking.Sockets;
using System.Runtime.InteropServices.WindowsRuntime;
using System.Net;
using System.IO;
using Windows.Storage;
using Windows.Networking.Connectivity;
using Windows.UI.Xaml.Controls;

namespace ArcShare.Server
{
	class ServerStatus
	{
		public string Message { get; set; }
		public uint ConnectionNumber { get; set; }
		public bool IsTransferring { get; set; }
	}

	class StatusChangedEventArgs : EventArgs
	{
		public ServerStatus Status { get; set; }
	}

	/// <summary>
	/// A HTTP Server by Copper
	/// </summary>
	class HttpServer : IDisposable
	{
		#region Members
		private readonly StreamSocketListener socketListener;
		ushort Port = 4000;
		private const int BufferSize = 1 << 16;
		private StorageFile IndexFile;
		private List<FileItem> Collection;
		private ServerStatus Status;
		private string Boundary;

		public string ServerAddress
		{
			get { return string.Format("http://{0}:{1}", GetLocalIp(), Port); }
		}
		#endregion

		#region Constructors
		public HttpServer(ushort p, StorageFile index, ItemCollection col) : this(p, index)
		{
			Collection = col.Cast<FileItem>().ToList();
		}
		public HttpServer(ushort p, StorageFile index) : this(p)
		{
			IndexFile = index;
		}
		public HttpServer(ushort p)
		{
			Port = p;
			this.socketListener = new StreamSocketListener();
			socketListener.ConnectionReceived += SocketListener_ConnectionReceived;

			Status = new ServerStatus() { ConnectionNumber = 0 };
		}
		#endregion

		#region Events
		public event EventHandler<StatusChangedEventArgs> StatusChanged;
		protected virtual void OnStatusChanged(StatusChangedEventArgs args)
		{
			StatusChanged?.Invoke(this, args);
		}

		private async void SocketListener_ConnectionReceived(StreamSocketListener sender, StreamSocketListenerConnectionReceivedEventArgs args)
		{
			StreamSocket socket = args.Socket;
			StringBuilder httpRequestBuilder = new StringBuilder();
			//读取输入数据流
			using (var input = socket.InputStream.AsStreamForRead())
			{
				try
				{
					var buf = Enumerable.Repeat((byte)0xFF, BufferSize).ToArray();
					var linebuffer = new StringBuilder();
					int count = 0;
					bool isHeaderRead = false, isPost = false;
					byte[] spliter = Encoding.ASCII.GetBytes("\r\n\r\n");
					HttpRequestHeader header = new HttpRequestHeader();

					var contentBuf = new byte[BufferSize];
					int contentProcessFlag = 0, boundaryCount = 0;
					Stream WriteStream = null;

					var folderToStore = await ApplicationData.Current.LocalCacheFolder.CreateFolderAsync("Arc Share", CreationCollisionOption.OpenIfExists);
					Debug.WriteLine(folderToStore.Path);

					while ((count = input.Read(buf, 0, buf.Length)) > 0)
					{   //从理论上来说，32kb的buffer无论如何都搞得完header
						contentBuf = buf;
						if (!isHeaderRead && buf.Intersect(spliter).Any())
						{
							isHeaderRead = true;
							string current = Encoding.ASCII.GetString(buf);
							int index = current.IndexOf("\r\n\r\n");
							string headerstr = current.Substring(0, index);
							header = HttpRequestHeader.Create(headerstr);

							Debug.WriteLine("Connection Received:" + header.RequestedUrl);
							if (header.Method == "GET") { await OnGet(socket, header); break; }
							else if (header.Method == "POST")
							{
								isPost = true;
								var firstContentBuf = buf.Skip(index + 4).ToArray();
								contentBuf = firstContentBuf;
								Boundary = header.ContentType.Substring(header.ContentType.LastIndexOf('=') + 1).Replace("\r", "");
							}
						}

						if (isPost)
						{
							var strs = ReadLines(contentBuf);
							foreach (var linebytes in strs)
							{
								string linestr = Encoding.UTF8.GetString(linebytes);
								if (linestr.StartsWith("--" + Boundary))
								{
									//判断是不是boundary
									boundaryCount++;

									if (linestr.StartsWith("--" + Boundary + "--"))
									{
										break;
										//结束了
									}

									if (boundaryCount != 1)
									{
										contentProcessFlag = 0;
									}

									if (WriteStream != null) await WriteStream.FlushAsync();
								}

								if (contentProcessFlag == 2 || contentProcessFlag == 0 || contentProcessFlag == 3)
								{
									contentProcessFlag++;
									continue;
								}
								else if (contentProcessFlag == 1)
								{
									string name = linestr.Substring(linestr.IndexOf("filename=") + 9).Replace("\r", "").Replace("\n", "").Replace("\"", "");
									if (WriteStream != null) await WriteStream.FlushAsync();
									var storageFile = await folderToStore.CreateFileAsync(name, CreationCollisionOption.GenerateUniqueName);
									WriteStream = (await storageFile.OpenStreamForWriteAsync());
								}
								else if (contentProcessFlag > 3)
								{
									//这里有文件内容了
									if (WriteStream != null)
									{
										await WriteStream.WriteAsync(linebytes, 0, linebytes.Length);
									}
								}
								contentProcessFlag++;
							}
						}
					}
					if (WriteStream != null)
					{
						await WriteStream.FlushAsync();
						WriteStream.Dispose();
					}
				}
				catch (Exception ex)
				{
					Debug.WriteLine(ex.Message, "Read Input Error");
				}
			}
		}
		#endregion

		List<byte> linebuf;
		private List<byte[]> ReadLines(byte[] buffer)
		{
			List<byte[]> lines = new List<byte[]>();
			if (linebuf == null) linebuf = new List<byte>();
			for (int i = 0; i < buffer.Length; i++)
			{
				linebuf.Add(buffer[i]);
				if (buffer[i] == 0x0A)
				{
					lines.Add(linebuf.ToArray());
					linebuf = new List<byte>();
				}
			}
			byte[] lineBreak = new byte[] { 0x0D, 0x0A };
			byte[] boundaryLine = Encoding.ASCII.GetBytes(Boundary);
			for (int i = 4; i < lines.Count - 1; i++)
			{
				bool b1 = Encoding.ASCII.GetString(lines[i + 1]).StartsWith("--" + Boundary);
				if (lines[i].Length == 2 && lines[i][1] == 0x0A && b1)
				{
					lines.RemoveAt(i);
				}
			}
			for (int i = 4; i < lines.Count - 1; i++)
			{
				bool b1 = Encoding.ASCII.GetString(lines[i + 1]).StartsWith("--" + Boundary);
				if (b1)
				{
					int len = lines[i].Length;
					bool b2 = (lines[i][len - 1] == 0x0A && lines[i][len - 2] == 0x0D);
					if (b2)
					{
						lines[i] = lines[i].SkipLast(2).ToArray();
					}
				}
			}

			List<string> list = new List<string>();
			foreach (var arr in lines)
			{
				string str = Encoding.UTF8.GetString(arr);
				list.Add(str);
			}

			return lines;
		}

		/// <summary>
		/// 在收到GET请求时的操作
		/// </summary>
		/// <param name="socket"></param>
		/// <param name="header"></param>
		/// <returns></returns>
		private async Task OnGet(StreamSocket socket, HttpRequestHeader header)
		{
			if (Collection != null && Collection.Count == 1)
			{ //只传一个文件就直接下载
				var file = Collection.First().File;
				await WriteResponseAsync(socket.OutputStream, file, HttpStatusCode.OK, true);
				return;
			}

			if (header.RequestedUrl == "/" || header.RequestedUrl == "/index")
			{
				//主页
				if (IndexFile == null)
				{
					header.RequestedUrl = "/index.html";
					IndexFile = await StorageFile.GetFileFromApplicationUriAsync(new Uri("ms-appx:///Server/content" + header.RequestedUrl));
				}
				await WriteResponseAsync(socket.OutputStream, IndexFile, HttpStatusCode.OK);
				return;
			}

			if (header.RequestedUrl.StartsWith("/get/"))
			{
				//DEBUG
				//Debug.Write(string.Format("{0}\n", httpRequestBuilder.ToString()));

				//文件内容
				int requested = Convert.ToInt32(header.RequestedUrl.Substring(5));
				var file = Collection[requested].File;

				//写输出流
				await WriteResponseAsync(socket.OutputStream, file, HttpStatusCode.OK, true);

				return;
			}

			if (header.RequestedUrl.StartsWith("/zip/"))
			{
				await WriteZipResponseAsync(socket.OutputStream);
				return;
			}

			try
			{
				var file = await StorageFile.GetFileFromApplicationUriAsync(new Uri("ms-appx:///Server/content" + header.RequestedUrl));
				using (var os = socket.OutputStream)
				{
					await WriteResponseAsync(os, file, HttpStatusCode.OK);
				}
			}
			catch (FileNotFoundException)
			{
				var notFound = await StorageFile.GetFileFromApplicationUriAsync(new Uri("ms-appx:///Server/content/404.html"));
				await WriteResponseAsync(socket.OutputStream, notFound, HttpStatusCode.NotFound);
			}

			return;
		}


		/// <summary>
		/// 将文件内容写入到输出流
		/// </summary>
		/// <param name="os"></param>
		/// <param name="file"></param>
		/// <param name="code"></param>
		/// <param name="isDownload"></param>
		/// <returns></returns>
		private async Task WriteResponseAsync(IOutputStream os, StorageFile file, HttpStatusCode code, bool isDownload = false)
		{
			using (var resp = os.AsStreamForWrite())
			{
				using (var source = await file.OpenStreamForReadAsync())
				{
					//Generate Response
					string mime = file.ContentType;
					string status = string.Format("HTTP/1.1 {0} {1}\r\n", (int)code, code.ToString());
					string header = string.Format(status +
									  "Date: " + DateTime.Now.ToString("R") + "\r\n" +
									  "Server: ArcShareTransfer/1.0\r\n" +
									  "Last-Modified: {2}\r\n" +
									  "Content-Length: {0}\r\n" +
									  //对多线程下载工具的妥协。。。
									  "Accept-Ranges: none\r\n" +
									  "Content-Type: {1}\r\n" +
									  "{3}" +
									  "Connection: close\r\n\r\n",
									  source.Length,
									  string.IsNullOrWhiteSpace(mime) ? "application/octet-stream" : mime,
									  (await file.GetBasicPropertiesAsync()).DateModified.ToString("r"),
									  isDownload ? "Content-Disposition: attachment; filename=\"" + file.Name + "\"\r\n" : null);

					byte[] encodedHeader = Encoding.UTF8.GetBytes(header);
					await resp.WriteAsync(encodedHeader, 0, encodedHeader.Length);
					var b = new byte[1 << 16]; //64kb
					int count = 0;
					while ((count = source.Read(b, 0, b.Length)) > 0)
					{
						await resp.WriteAsync(b, 0, count);
					}
					await resp.FlushAsync();
				}
			}
		}

		
		private byte[] zipheader = { 0x50, 0x4B, 0x03, 0x04, 0x14, 0x00, 0x00, 0x08, 0x00, 0x00 };
		//                           Signature(4bytes)       version20   flags(enable UTF8)     no-compresion
		private byte[] cdheader = { 0x50, 0x4B, 0x01, 0x02, 0x14, 0x00, 0x14, 0x00, 0x00, 0x08, 0x00, 0x00 };
		//							//Signature             Version		PKVersion	Flags		no-compression
		/// <summary>
		/// 把文件列表都打成ZIP包再实时将数据流输出到HTTP Response
		/// </summary>
		/// <param name="os"></param>
		/// <returns></returns>
		private async Task WriteZipResponseAsync(IOutputStream os)
		{
			using (var resp = os.AsStreamForWrite())
			{
				var crc = new Crc32();
				List<byte> cdfheaders = new List<byte>();
				int wroteLength = 0, offset = 0;
				string linebreak = "\r\n";
				var linebreakbytes = Encoding.Default.GetBytes("\r\n");

				string date = DateTime.Now.ToString("R");
				string header = "HTTP/1.1 200 OK\r\n" +
								  "Date: " + date + "\r\n" +
								  "Server: ArcShareTransfer/1.0\r\n" +
								  "Last-Modified: " + date + "\r\n" +
								  "Transfer-Encoding: chunked\r\n" +
								  "Accept-Ranges: none\r\n" +
								  "Content-Type: application/zip\r\n" +
								  "Content-Disposition: attachment; filename=\"ArcShare.zip\"\r\n" +
								  "Connection: keep-alive\r\n\r\n";
				byte[] encodedHeader = Encoding.UTF8.GetBytes(header);
				//写header
				await resp.WriteAsync(encodedHeader, 0, encodedHeader.Length);
				//writedLength += encodedHeader.Length;

				foreach (var item in Collection) //对每个文件
				{
					int headerStart = wroteLength;
					//chunked header 1: 每个本地header有30b
					var chunkedb1 = Encoding.ASCII.GetBytes(Convert.ToString(30, 16) + linebreak);
					await resp.WriteAsync(chunkedb1, 0, chunkedb1.Length);
					//0x0 - 0x9
					await resp.WriteAsync(zipheader, 0, zipheader.Length);
					wroteLength += zipheader.Length;
					//0xA - 0xD
					var lastmod = (await item.File.GetBasicPropertiesAsync()).DateModified;
					int rawlastmod = (lastmod.Hour << 11) + (lastmod.Minute << 5) + (lastmod.Second / 2);
					byte[] b1 = BitConverter.GetBytes((ushort)rawlastmod);
					int rawlastdate = ((lastmod.Year - 1980) << 9) + (lastmod.Month << 5) + lastmod.Day;
					byte[] b2 = BitConverter.GetBytes((ushort)rawlastdate);
					//写文件修改信息
					await resp.WriteAsync(b1, 0, b1.Length);
					wroteLength += b1.Length;
					await resp.WriteAsync(b2, 0, b2.Length);
					wroteLength += b2.Length;

					cdfheaders.AddRange(cdheader);
					cdfheaders.AddRange(b1);
					cdfheaders.AddRange(b2);

					using (var input = await item.File.OpenStreamForReadAsync())
					{
						//CRC32校验
						var hash = crc.ComputeHash(input).Reverse().ToArray();
						await resp.WriteAsync(hash, 0, hash.Length);
						wroteLength += hash.Length;
						cdfheaders.AddRange(hash);

						//因为没压缩，所以compressed size和uncompressed size是一样的
						uint size = Convert.ToUInt32(input.Length);
						var sizeb = BitConverter.GetBytes(size).ToArray();
						for (int i = 1; i <= 2; i++)
						{
							await resp.WriteAsync(sizeb, 0, sizeb.Length);
							wroteLength += sizeb.Length;
							cdfheaders.AddRange(sizeb);
						}

						//finename length (2bits)
						var filenameUtfb = Encoding.UTF8.GetBytes(item.FullName);
						ushort namelength = (ushort)filenameUtfb.Length;
						var namelenb = BitConverter.GetBytes(namelength);
						await resp.WriteAsync(namelenb, 0, namelenb.Length);
						wroteLength += namelenb.Length;
						cdfheaders.AddRange(namelenb);

						bool isExtraFieldNeeded = false;
						List<byte> extrafield = null;
						if (filenameUtfb.Length > item.FullName.Length)
						{   //含有非ASCII编码 
							isExtraFieldNeeded = true;
							//先把extra field构造出来。。。才知道length
							//TSize 就是 totalefLen - 2;
							extrafield = new List<Byte> { 0x75, 0x70, 0xFF, 0xFF, 0x01 };
							//										  这两个换成TSize

							var nameCRC = crc.ComputeHash(filenameUtfb).Reverse();
							extrafield.AddRange(nameCRC);
							extrafield.AddRange(filenameUtfb);
							var totalfLen = extrafield.Count;
							var tsizeb = BitConverter.GetBytes((ushort)(totalfLen - 4));
							extrafield[2] = tsizeb[0]; extrafield[3] = tsizeb[1]; //TSize

							//处理extra field length
							ushort totalExtraLenth = (ushort)extrafield.Count;
							var eflenb = BitConverter.GetBytes(totalExtraLenth);
							await resp.WriteAsync(eflenb, 0, eflenb.Length);
							wroteLength += eflenb.Length;
							cdfheaders.AddRange(eflenb);
						}
						else
						{   //全是ASCII编码，不需要0x7075头
							var extrafieldLenb = BitConverter.GetBytes((ushort)0);
							await resp.WriteAsync(extrafieldLenb, 0, extrafieldLenb.Length);
							wroteLength += extrafieldLenb.Length;
							cdfheaders.AddRange(extrafieldLenb);
						}

						cdfheaders.AddRange(new byte[] { 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x20, 0x00, 0x00, 0x00 });
						cdfheaders.AddRange(BitConverter.GetBytes(headerStart));


						await resp.WriteAsync(linebreakbytes, 0, linebreakbytes.Length);
						//end of chunked header 1

						//chunked header 2 filename & extra field
						var namebytes = Encoding.UTF8.GetBytes(item.FullName);
						var chunkedb2Count = namebytes.Length + (isExtraFieldNeeded ? extrafield.Count : 0);
						var chunkedb2 = Encoding.ASCII.GetBytes(Convert.ToString(chunkedb2Count, 16) + linebreak);
						await resp.WriteAsync(chunkedb2, 0, chunkedb2.Length);
						await resp.WriteAsync(namebytes, 0, namebytes.Length);
						wroteLength += namebytes.Length;
						cdfheaders.AddRange(namebytes);
						if (isExtraFieldNeeded)
						{
							await resp.WriteAsync(extrafield.ToArray(), 0, extrafield.Count);
							wroteLength += extrafield.Count;
							cdfheaders.AddRange(extrafield);
						}

						await resp.WriteAsync(linebreakbytes, 0, linebreakbytes.Length);
						//end of chunked header 2
					}
					using (var source = await item.File.OpenStreamForReadAsync())
					{
						//content
						var chunkedbn = Encoding.ASCII.GetBytes(Convert.ToString((uint)(await item.File.GetBasicPropertiesAsync()).Size, 16) + linebreak);
						await resp.WriteAsync(chunkedbn, 0, chunkedbn.Length);

						var b = new byte[1 << 16]; //64kb buffer
						int count = 0;
						while ((count = source.Read(b, 0, b.Length)) > 0)
						{
							await resp.WriteAsync(b, 0, count);
							wroteLength += count;
						}
						await resp.WriteAsync(linebreakbytes, 0, linebreakbytes.Length);
						offset += wroteLength;
					}
				}

				uint cdstartoffset = (uint)wroteLength;
				//写Central directory
				var chunkedb3 = Encoding.ASCII.GetBytes(Convert.ToString(cdfheaders.Count, 16) + linebreak);
				await resp.WriteAsync(chunkedb3, 0, chunkedb3.Length);
				await resp.WriteAsync(cdfheaders.ToArray(), 0, cdfheaders.Count);
				await resp.WriteAsync(linebreakbytes, 0, linebreakbytes.Length);
				//end of central directory record
				List<byte> endcdr = new List<byte>();
				endcdr.AddRange(new byte[] { 0x50, 0x4B, 0x05, 0x06, 0x00, 0x00, 0x00, 0x00 });
				endcdr.AddRange(BitConverter.GetBytes((ushort)Collection.Count));
				endcdr.AddRange(BitConverter.GetBytes((ushort)Collection.Count)); //没错，就是要两遍
				endcdr.AddRange(BitConverter.GetBytes((uint)cdfheaders.Count));
				endcdr.AddRange(BitConverter.GetBytes(cdstartoffset));
				endcdr.AddRange(new byte[] { 0x00, 0x00 });

				var chunkedb4 = Encoding.ASCII.GetBytes(Convert.ToString(endcdr.Count, 16) + linebreak);
				await resp.WriteAsync(chunkedb4, 0, chunkedb4.Length);
				await resp.WriteAsync(endcdr.ToArray(), 0, endcdr.Count);
				await resp.WriteAsync(linebreakbytes, 0, linebreakbytes.Length);
				var chunkedb5 = Encoding.ASCII.GetBytes("0\r\n\r\n");
				await resp.WriteAsync(chunkedb5, 0, chunkedb5.Length);

				await resp.FlushAsync();
			}
		}
		/// <summary>
		/// Start the Socket Server
		/// </summary>
		public async void Start()
		{
			await socketListener.BindServiceNameAsync(Port.ToString());
			Debug.WriteLine(string.Format("HTTP Server is running at {0}", ServerAddress));
		}

		/// <summary>
		/// Release the server
		/// </summary>
		public void Dispose()
		{
			socketListener.Dispose();
			Collection = null;
		}

		/// <summary>
		/// 获取本地IP地址
		/// </summary>
		/// <returns></returns>
		public string GetLocalIp()
		{
			var icp = NetworkInformation.GetInternetConnectionProfile();

			if (icp?.NetworkAdapter == null) return null;
			var hostname =
				NetworkInformation.GetHostNames()
					.SingleOrDefault(
						hn =>
							hn.IPInformation?.NetworkAdapter != null && hn.IPInformation.NetworkAdapter.NetworkAdapterId
							== icp.NetworkAdapter.NetworkAdapterId);

			// the ip address
			return hostname?.CanonicalName;
		}

		public string GetNetworkName()
		{
			var info = NetworkInformation.GetInternetConnectionProfile();
			if (info.NetworkAdapter == null) return null;
			var networkName = info.GetNetworkNames().First();
			return networkName;
		}
	}
}
