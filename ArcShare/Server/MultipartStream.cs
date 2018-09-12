using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Windows.Storage.Streams;

namespace ArcShare.Server
{
	class MultipartStream
	{
		// ------STATIC MENBERS-------
		public const byte CR = 0x0D;
		public const byte LF = 0x0A;
		public const byte DASH = 0x2D;

		public const int HEADER_PART_SIZE_MAX = 10240;
		protected const int DEFAULT_BUFSIZE = 4096;

		protected static readonly byte[] HEADER_SEPARATOR = new byte[] { CR, LF, CR, LF };
		protected static readonly byte[] FIELD_SEPARATOR = new byte[] { CR, LF };
		protected static readonly byte[] STREAM_TERMINATOR = new byte[] { DASH, DASH };
		protected static readonly byte[] BOUNDARY_PREFIX = { CR, LF, DASH, DASH };

		// -------DATA MEMBERS---------
		private readonly Stream Input;
		private int BoundaryLength;

		/**
		* The amount of data, in bytes, that must be kept in the buffer in order
		* to detect delimiters reliably.
		*/
		private readonly int KeepRegion;

		private readonly byte[] Boundary;
		private readonly int[] BoundaryTable; //The table for Knuth-Morris-Pratt search algorithm.

		private readonly int BufSize;
		private readonly byte[] Buffer;

		private int Head; // The index of first valid char in the buffer, 0 <= head < bufSize
		private int Tail; // The index of last valid char in the buffer + 1, 0 <= head <= bufSize

		private string HeaderEncoding { get; set; }

		//--------CONSTRUCTOR----------
		/// <summary>
		/// Construct a MultipartStream with a custon size buffer
		/// </summary>
		/// <param name="input">data source</param>
		/// <param name="boundary">the token used for dividing the stream into encapsulations</param>
		/// <param name="bufSize">the size of the buffer to be used</param>
		public MultipartStream(Stream input, byte[] boundary, int bufSize)
		{
			if (boundary == null) throw new IllegalBoundaryException("boundary may not be null");
			//前置CR/LF到boundary来砍掉body数据尾部的CR/LF???
			this.BoundaryLength = boundary.Length + BOUNDARY_PREFIX.Length;
			if (bufSize < this.BoundaryLength + 1)
				throw new IllegalBoundaryException("The buffer size specified for the MultipartStream is too small");

			this.Input = input;
			this.BufSize = Math.Max(bufSize, BoundaryLength * 2);
			this.Buffer = new byte[this.BufSize];

			this.Boundary = new byte[this.BoundaryLength];
			this.BoundaryTable = new int[this.BoundaryLength + 1];
			this.KeepRegion = this.Boundary.Length;

			BOUNDARY_PREFIX.CopyTo(this.Boundary, 0);
			boundary.CopyTo(this.Boundary, BOUNDARY_PREFIX.Length);

			ComputeBoundaryTable();

			this.Head = 0;
			this.Tail = 0;
		}

		public MultipartStream(Stream input, byte[] boundary) : this(input, boundary, DEFAULT_BUFSIZE) { }


		//----------PUBLIC METHODS----------

		/// <summary>
		/// reads a byte from the buffer and refills it as necessary
		/// </summary>
		/// <returns>the next byte from the input stream</returns>
		public byte ReadByte()
		{
			// buffer depleated ?
			if (Head == Tail)
			{
				Head = 0;
				//refill
				Tail = Input.Read(Buffer, Head, BufSize);
				if (Tail == -1)
				{
					// no more data available
					throw new IOException("No more data is available");
				}
			}
			return Buffer[Head++];
		}

		public bool ReadBoundary()
		{
			byte[] marker = new byte[2];
			bool nextChunk = false;

			Head += BoundaryLength;

			try
			{
				marker[0] = ReadByte();
				if (marker[0] == LF)
				{
					// Work around IE5 Mac bug with input type=image.
					// Because the boundary delimiter, not including the trailing
					// CRLF, must not appear within any file (RFC 2046, section
					// 5.1.1), we know the missing CR is due to a buggy browser
					// rather than a file containing something similar to a
					// boundary.
					return true;
				}
				marker[1] = ReadByte();

				if (ArrayEquals(marker, STREAM_TERMINATOR, 2)) nextChunk = false;
				else if (ArrayEquals(marker, FIELD_SEPARATOR, 2)) nextChunk = true;
				else throw new MalformedStreamException("Unexpted characters follow a boundary");
			}
			catch (FileUploadException e) { throw e; }
			catch (IOException e)
			{
				throw new MalformedStreamException("Stream ended unexpectedly");
			}

			return nextChunk;
		}

		/// <summary>
		/// Changes the boundary token used for partitioning the stream.
		/// </summary>
		/// <param name="boundary">boundary The boundary to be used for parsing of the nested stream</param>
		public void SetBoundary(byte[] boundary)
		{
			if (boundary.Length != BoundaryLength - BOUNDARY_PREFIX.Length)
				throw new IllegalBoundaryException("The length of a boundary token cannot be changed");
			boundary.CopyTo(this.Boundary, BOUNDARY_PREFIX.Length);

			ComputeBoundaryTable();
		}

		/// <summary>
		/// Compute the table used for Knuth-Morris-Pratt search algorithm.
		/// </summary>
		private void ComputeBoundaryTable()
		{
			int position = 2;
			int candidate = 0;

			BoundaryTable[0] = -1;
			BoundaryTable[1] = 0;

			while (position <= BoundaryLength)
			{
				if (Boundary[position - 1] == Boundary[candidate])
				{
					BoundaryTable[position] = candidate + 1;
					candidate++;
					position++;
				}
				else if (candidate > 0)
				{
					candidate = BoundaryTable[candidate];
				}
				else
				{
					BoundaryTable[position] = 0;
					position++;
				}
			}
		}

		public string ReadHeaders()
		{
			int i = 0;
			byte b;
			MemoryStream s = new MemoryStream();
			int size = 0;

			while (i < HEADER_SEPARATOR.Length)
			{
				b = ReadByte();

				if (++size > HEADER_PART_SIZE_MAX)
				{
					throw new InvalidDataException("Header section has more than " + HEADER_PART_SIZE_MAX + " bytes ((maybe it is not properly terminated)");
				}

				if (b == HEADER_SEPARATOR[i])
				{
					i++;
				}
				else
				{
					i = 0;
				}

				s.WriteByte(b);
			}

			string headers = string.Empty;
			if (HeaderEncoding != null)
			{
				try
				{
					headers = Encoding.GetEncoding(HeaderEncoding).GetString(s.GetBuffer());
				}
				catch (Exception e)
				{
					headers = Encoding.Default.GetString(s.GetBuffer());
				}
			}
			else
			{
				headers = Encoding.Default.GetString(s.GetBuffer());
			}

			return headers;
		}

		//public int ReadBodyData(IOutputStream output)
		//{
		//return (int) 
		//}

		public static bool ArrayEquals(byte[] a, byte[] b, int count)
		{
			for (int i = 0; i < count; i++)
			{
				if (a[i] != b[i]) return false;
			}
			return true;
		}
		/// <summary>
		/// Search for a byte of specified value in the buffer starting at the specified position.
		/// </summary>
		/// <param name="value"></param>
		/// <param name="pos"></param>
		/// <returns></returns>
		protected int FindByte(byte value, int pos)
		{
			for (int i = pos; i < Tail; i++)
			{
				if (Buffer[i] == value) return i;
			}
			return -1;
		}

		/// <summary>
		/// Search for the boundary in the buffer region delimited by head and tail
		/// </summary>
		/// <returns></returns>
		protected int FindSeparator()
		{
			int bufferPos = this.Head;
			int tablePos = 0;

			while (bufferPos < this.Tail)
			{
				while (tablePos >= 0 && Buffer[bufferPos] != Boundary[tablePos])
				{
					tablePos = BoundaryTable[tablePos];
				}
				bufferPos++;
				tablePos++;
				if (tablePos == BoundaryLength)
				{
					return bufferPos - BoundaryLength;
				}
			}
			return -1;
		}

		public class MalformedStreamException : IOException
		{
			private static readonly long serialVersionUID = 6466926458059796677L;
			public MalformedStreamException() : base() { }
			public MalformedStreamException(string message) : base(message) { }
		}

		public class IllegalBoundaryException : IOException
		{
			private static readonly long serialVersionUID = -161533165102632918L;
			public IllegalBoundaryException() : base() { }
			public IllegalBoundaryException(string message) : base(message) { }
		}

		public class FileUploadException : Exception
		{
			private static readonly long serialVersionUID = -4222909057964038517L;
			public FileUploadException() : base() { }
			public FileUploadException(string message) : base(message) { }
		}

		//internal class ItemInputStream : IInputStream, IDisposable
		//{
		//	private long Total;
		//	private int Pad;
		//	private int Pos;
		//	private bool Closed;

		//	ItemInputStream() { }

		//	private void FindSeparator()
		//	{
		//		MultipartStream.this.
		//	}
	}
}
