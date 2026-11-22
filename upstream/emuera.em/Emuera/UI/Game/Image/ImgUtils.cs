using MinorShift.Emuera.Runtime.Utils;
using System.Drawing;
using System.IO;

namespace MinorShift.Emuera.UI.Game.Image;

static class ImgUtils
{
	public static Bitmap LoadImage(string filepath)
	{
		if (!File.Exists(filepath))
		{
			return null;
		}
		Bitmap bmp = null;

		if (Path.GetExtension(filepath).ToUpperInvariant() == ".WEBP")
		{
			using (WebP webp = new())
				bmp = webp.Load(filepath);

			if (bmp == null)
			{
				return null;
			}
		}
		else
		{
			bmp = new Bitmap(filepath);
			if (bmp == null)
			{
				return null;
			}
		}

		return bmp;
	}

	/// <summary>
	/// 画像をデコードせず、ファイルヘッダから幅・高さだけ読む。
	///
	/// resources の CSV は「画像の寸法」だけを知りたいために全ファイルをデコードしており、
	/// 実データ (3000 枚超) では起動に 10 秒以上かかっていた。寸法はヘッダだけで分かるので、
	/// 実ピクセルは初回描画時 (<see cref="ConstImage.Load"/>) まで読まない。
	/// 判定できない形式・壊れたファイルは false を返し、呼び出し側が従来どおりデコードする。
	/// </summary>
	public static bool TryReadImageSize(string filepath, out int width, out int height)
	{
		width = 0;
		height = 0;
		try
		{
			if (!File.Exists(filepath))
				return false;
			using var fs = new FileStream(filepath, FileMode.Open, FileAccess.Read, FileShare.Read, 4096);
			var head = new byte[32];
			int read = fs.Read(head, 0, head.Length);
			if (read < 10)
				return false;

			// PNG: 89 50 4E 47 0D 0A 1A 0A + len(4) + "IHDR" + w(4) + h(4)
			if (head[0] == 0x89 && head[1] == 0x50 && head[2] == 0x4E && head[3] == 0x47
				&& head[12] == (byte)'I' && head[13] == (byte)'H' && head[14] == (byte)'D' && head[15] == (byte)'R')
			{
				width = (head[16] << 24) | (head[17] << 16) | (head[18] << 8) | head[19];
				height = (head[20] << 24) | (head[21] << 16) | (head[22] << 8) | head[23];
				return width > 0 && height > 0;
			}

			// GIF: "GIF87a"/"GIF89a" + w(2 LE) + h(2 LE)
			if (head[0] == (byte)'G' && head[1] == (byte)'I' && head[2] == (byte)'F')
			{
				width = head[6] | (head[7] << 8);
				height = head[8] | (head[9] << 8);
				return width > 0 && height > 0;
			}

			// BMP: "BM" + ... + DIB ヘッダサイズ(4 LE) + w(4 LE) + h(4 LE)
			if (head[0] == (byte)'B' && head[1] == (byte)'M' && read >= 26)
			{
				width = System.BitConverter.ToInt32(head, 18);
				height = System.Math.Abs(System.BitConverter.ToInt32(head, 22));
				return width > 0 && height > 0;
			}

			// WebP: RIFF....WEBP + VP8 / VP8L / VP8X
			if (head[0] == (byte)'R' && head[1] == (byte)'I' && head[2] == (byte)'F' && head[3] == (byte)'F'
				&& head[8] == (byte)'W' && head[9] == (byte)'E' && head[10] == (byte)'B' && head[11] == (byte)'P')
				return TryReadWebpSize(fs, head, read, out width, out height);

			// JPEG: SOFn セグメントに寸法がある
			if (head[0] == 0xFF && head[1] == 0xD8)
				return TryReadJpegSize(fs, out width, out height);
		}
		catch
		{
			// 壊れたファイルは呼び出し側のデコード経路に任せる
		}
		return false;
	}

	static bool TryReadWebpSize(FileStream fs, byte[] head, int read, out int width, out int height)
	{
		width = 0;
		height = 0;
		if (read < 30)
			return false;
		string chunk = System.Text.Encoding.ASCII.GetString(head, 12, 4);
		if (chunk == "VP8X")
		{
			// flags(1) + reserved(3) の後、canvas w-1(3 LE) / h-1(3 LE)
			width = 1 + (head[24] | (head[25] << 8) | (head[26] << 16));
			height = 1 + (head[27] | (head[28] << 8) | (head[29] << 16));
			return true;
		}
		if (chunk == "VP8L")
		{
			// 0x2F のあと 14bit + 14bit
			if (head[20] != 0x2F)
				return false;
			int bits = head[21] | (head[22] << 8) | (head[23] << 16) | (head[24] << 24);
			width = (bits & 0x3FFF) + 1;
			height = ((bits >> 14) & 0x3FFF) + 1;
			return true;
		}
		if (chunk == "VP8 ")
		{
			// frame tag(3) + start code(3) + w(2 LE) + h(2 LE)
			if (head[23] != 0x9D || head[24] != 0x01 || head[25] != 0x2A)
				return false;
			width = (head[26] | (head[27] << 8)) & 0x3FFF;
			height = (head[28] | (head[29] << 8)) & 0x3FFF;
			return width > 0 && height > 0;
		}
		return false;
	}

	static bool TryReadJpegSize(FileStream fs, out int width, out int height)
	{
		width = 0;
		height = 0;
		fs.Position = 2;
		var pair = new byte[2];
		while (true)
		{
			// マーカー (0xFF) まで読み飛ばす
			int b = fs.ReadByte();
			if (b < 0)
				return false;
			if (b != 0xFF)
				continue;
			int marker;
			do { marker = fs.ReadByte(); } while (marker == 0xFF);
			if (marker < 0)
				return false;
			if (marker == 0xD8 || marker == 0x01 || (marker >= 0xD0 && marker <= 0xD7))
				continue;
			if (marker == 0xD9 || marker == 0xDA)
				return false;
			if (fs.Read(pair, 0, 2) != 2)
				return false;
			int len = (pair[0] << 8) | pair[1];
			if (len < 2)
				return false;
			bool isSof = marker >= 0xC0 && marker <= 0xCF
				&& marker != 0xC4 && marker != 0xC8 && marker != 0xCC;
			if (isSof)
			{
				// Lf(2) の後は precision(1) → height(2) → width(2)
				if (fs.ReadByte() < 0) return false;
				if (fs.Read(pair, 0, 2) != 2) return false;
				height = (pair[0] << 8) | pair[1];
				if (fs.Read(pair, 0, 2) != 2) return false;
				width = (pair[0] << 8) | pair[1];
				return width > 0 && height > 0;
			}
			fs.Seek(len - 2, SeekOrigin.Current);
		}
	}
}
