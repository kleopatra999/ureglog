using System.Collections.Generic;
using System.IO;

namespace UnityVS
{
	static class Extensions
	{
		public static void WriteTo(this Stream stream, Stream dest)
		{
			var buffer = new byte[8 * 1024];
			int length;

			while ((length = stream.Read(buffer, 0, buffer.Length)) > 0)
				dest.Write(buffer, 0, length);
		}

		public static HashSet<T> ToHashSet<T>(this IEnumerable<T> self)
		{
			return self.ToHashSet(EqualityComparer<T>.Default);
		}

		public static HashSet<T> ToHashSet<T>(this IEnumerable<T> self, IEqualityComparer<T> comparer)
		{
			return new HashSet<T>(self, comparer);
		}
	}
}