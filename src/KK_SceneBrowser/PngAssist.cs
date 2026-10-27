using System;
using System.IO;
using System.Reflection;
using UnityEngine;
using Object = UnityEngine.Object;

namespace KK_SceneBrowser;

internal static class PngAssist
{
	public static Action<string> OnError;

	private static MethodInfo _loadImage;

	private static bool _loadTried;

	private static int _errs;

	public static string LongPath(string path)
	{
		try
		{
			if (!string.IsNullOrEmpty(path) && path.Length >= 240 && !path.StartsWith("\\\\?\\", StringComparison.Ordinal))
			{
				return "\\\\?\\" + Path.GetFullPath(path);
			}
		}
		catch (Exception)
		{
		}
		return path;
	}

	public static Texture2D LoadTexture(string path)
	{
		try
		{
			if (string.IsNullOrEmpty(path))
			{
				return null;
			}
			byte[] array = ReadPngBytes(path);
			if (array == null || array.Length < 64)
			{
				Report("short png: " + path, force: false);
				return null;
			}
			Texture2D val = new Texture2D(2, 2, (TextureFormat)4, false);
			((Texture)val).wrapMode = (TextureWrapMode)1;
			((Texture)val).filterMode = (FilterMode)1;
			if (!TryLoadImage(val, array))
			{
				Object.Destroy((Object)(object)val);
				return null;
			}
			return val;
		}
		catch (Exception ex)
		{
			Report("decode: " + ex.GetType().Name + " " + ex.Message + " @ " + path, force: false);
			return null;
		}
	}

	private static void Report(string msg, bool force)
	{
		if (OnError == null)
		{
			return;
		}
		if (!force && _errs >= 8)
		{
			return;
		}
		_errs++;
		try
		{
			OnError(msg);
		}
		catch (Exception)
		{
		}
	}

	private static bool TryLoadImage(Texture2D tex, byte[] data)
	{
		if (!_loadTried)
		{
			_loadTried = true;
			Resolve();
		}
		if (_loadImage == null)
		{
			return false;
		}
		try
		{
			ParameterInfo[] parameters = _loadImage.GetParameters();
			if (_loadImage.IsStatic)
			{
				object[] array = ((parameters.Length >= 3) ? new object[3] { tex, data, false } : new object[2] { tex, data });
				return (bool)_loadImage.Invoke(null, array);
			}
			object[] array2 = ((parameters.Length >= 2) ? new object[2] { data, false } : new object[1] { data });
			return (bool)_loadImage.Invoke(tex, array2);
		}
		catch (Exception ex)
		{
			Report("invoke " + _loadImage.DeclaringType.Name + "." + _loadImage.Name + ": " + ex.GetType().Name + " " + ex.Message, force: false);
			return false;
		}
	}

	private static void Resolve()
	{
		try
		{
			MethodInfo method = typeof(Texture2D).GetMethod("LoadImage", new Type[2] { typeof(byte[]), typeof(bool) });
			if (method == null)
			{
				method = typeof(Texture2D).GetMethod("LoadImage", new Type[1] { typeof(byte[]) });
			}
			if (method != null)
			{
				_loadImage = method;
				Report("api: Texture2D.LoadImage", force: false);
				return;
			}
		}
		catch (Exception)
		{
		}
		try
		{
			Type type = FindType("UnityEngine.ImageConversion");
			if (type != null)
			{
				MethodInfo method2 = type.GetMethod("LoadImage", new Type[3] { typeof(Texture2D), typeof(byte[]), typeof(bool) });
				if (method2 == null)
				{
					method2 = type.GetMethod("LoadImage", new Type[2] { typeof(Texture2D), typeof(byte[]) });
				}
				if (method2 != null)
				{
					_loadImage = method2;
					Report("api: ImageConversion.LoadImage", force: false);
					return;
				}
			}
		}
		catch (Exception)
		{
		}
		Report("no png api", force: true);
	}

	private static Type FindType(string name)
	{
		Type type = null;
		try
		{
			type = Type.GetType(name + ", UnityEngine.ImageConversionModule") ?? Type.GetType(name + ", UnityEngine");
		}
		catch (Exception)
		{
		}
		if (type != null)
		{
			return type;
		}
		try
		{
			Assembly[] assemblies = AppDomain.CurrentDomain.GetAssemblies();
			for (int i = 0; i < assemblies.Length; i++)
			{
				try
				{
					type = assemblies[i].GetType(name, false);
				}
				catch (Exception)
				{
					type = null;
				}
				if (type != null)
				{
					return type;
				}
			}
		}
		catch (Exception)
		{
		}
		return null;
	}

	private static int Fill(Stream st, byte[] buf, int need)
	{
		int i;
		int num;
		for (i = 0; i < need; i += num)
		{
			num = st.Read(buf, i, need - i);
			if (num <= 0)
			{
				break;
			}
		}
		return i;
	}

	private static byte[] ReadPngBytes(string path)
	{
		using FileStream fileStream = new FileStream(LongPath(path), FileMode.Open, FileAccess.Read, FileShare.ReadWrite, 65536);
		long length = fileStream.Length;
		if (length <= 0)
		{
			return null;
		}
		byte[] array = new byte[8];
		if (Fill(fileStream, array, 8) != 8)
		{
			return null;
		}
		if (array[0] != 137 || array[1] != 80)
		{
			return null;
		}
		byte[] array2 = new byte[8];
		long num = -1L;
		while (Fill(fileStream, array2, 8) == 8)
		{
			long num2 = (long)(((ulong)array2[0] << 24) | ((ulong)array2[1] << 16) | ((ulong)array2[2] << 8) | array2[3]);
			bool flag = array2[4] == 73 && array2[5] == 69 && array2[6] == 78 && array2[7] == 68;
			long num3 = fileStream.Position + num2 + 4;
			if (num3 > length || num3 < 0)
			{
				break;
			}
			fileStream.Position = num3;
			if (flag)
			{
				num = num3;
				break;
			}
		}
		if (num <= 8 || num > length || num > int.MaxValue)
		{
			num = length;
		}
		byte[] array3 = new byte[(int)num];
		fileStream.Position = 0L;
		int i;
		int num4;
		for (i = 0; i < array3.Length; i += num4)
		{
			num4 = fileStream.Read(array3, i, array3.Length - i);
			if (num4 <= 0)
			{
				break;
			}
		}
		if (i != array3.Length)
		{
			Array.Resize(ref array3, i);
		}
		return array3;
	}
}
