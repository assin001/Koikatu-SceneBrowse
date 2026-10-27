using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace KK_SceneBrowser
{
	/// <summary>
	/// 扁平化深色主题：统一色板 + 运行时生成的圆角九宫格贴图。
	/// </summary>
	internal static class UiTheme
	{
		// ── 色板 ───────────────────────────────────────────────
		public static readonly Color Window = new Color32(0x12, 0x18, 0x1F, 0xFA);
		public static readonly Color Page = new Color32(0x17, 0x1E, 0x26, 0xFF);
		public static readonly Color Surface = new Color32(0x1C, 0x24, 0x2E, 0xFF);
		public static readonly Color Sunken = new Color32(0x15, 0x1C, 0x23, 0xFF);
		public static readonly Color SurfaceAlt = new Color32(0x22, 0x2C, 0x37, 0xFF);
		public static readonly Color Field = new Color32(0x20, 0x2A, 0x35, 0xFF);
		public static readonly Color Hover = new Color32(0x2B, 0x38, 0x46, 0xFF);
		public static readonly Color Border = new Color32(0x33, 0x46, 0x5A, 0xFF);
		public static readonly Color BorderSoft = new Color32(0x26, 0x31, 0x3D, 0xFF);
		public static readonly Color Text = new Color32(0xE7, 0xED, 0xF3, 0xFF);
		public static readonly Color TextSub = new Color32(0xA6, 0xB6, 0xC4, 0xFF);
		public static readonly Color TextDim = new Color32(0x74, 0x86, 0x9A, 0xFF);

		public static readonly Color Accent = new Color32(0x2E, 0x9B, 0xE0, 0xFF);
		public static readonly Color AccentSoft = new Color32(0x2E, 0x9B, 0xE0, 0x3D);
		public static readonly Color AccentLine = new Color32(0x2E, 0x9B, 0xE0, 0x99);
		public static readonly Color Green = new Color32(0x2F, 0xA9, 0x6B, 0xFF);
		public static readonly Color GreenSoft = new Color32(0x2F, 0xA9, 0x6B, 0x3D);
		public static readonly Color Gold = new Color32(0xE8, 0xB3, 0x3A, 0xFF);
		public static readonly Color Danger = new Color32(0xE0, 0x5B, 0x5B, 0xFF);
		public static readonly Color ThumbBg = new Color32(0x0F, 0x15, 0x1B, 0xFF);

		public const int RadiusPanel = 16;
		public const int RadiusCard = 12;
		public const int RadiusItem = 9;
		public const int RadiusChip = 8;

		private static readonly Dictionary<int, Sprite> Cache = new Dictionary<int, Sprite>();

		/// <summary>圆角矩形九宫格贴图（白色，靠 Image.color 上色）。</summary>
		public static Sprite Rounded(int radius)
		{
			if (radius < 1)
			{
				radius = 1;
			}
			Sprite cached;
			if (Cache.TryGetValue(radius, out cached) && (Object)(object)cached != (Object)null)
			{
				return cached;
			}
			int border = radius + 1;
			int size = radius * 2 + 6;
			Texture2D tex = new Texture2D(size, size, TextureFormat.ARGB32, false);
			tex.wrapMode = TextureWrapMode.Clamp;
			tex.filterMode = FilterMode.Bilinear;
			Color32[] buf = new Color32[size * size];
			float half = size * 0.5f;
			float inner = half - 1.5f;
			float r = radius;
			for (int y = 0; y < size; y++)
			{
				for (int x = 0; x < size; x++)
				{
					float dx = Mathf.Abs(x + 0.5f - half) - (inner - r);
					float dy = Mathf.Abs(y + 0.5f - half) - (inner - r);
					float ax = Mathf.Max(dx, 0f);
					float ay = Mathf.Max(dy, 0f);
					float dist = Mathf.Sqrt(ax * ax + ay * ay) + Mathf.Min(Mathf.Max(dx, dy), 0f) - r;
					float a = Mathf.Clamp01(0.5f - dist);
					buf[y * size + x] = new Color32(255, 255, 255, (byte)Mathf.RoundToInt(a * 255f));
				}
			}
			tex.SetPixels32(buf);
			tex.Apply();
			Sprite sp = Sprite.Create(tex, new Rect(0f, 0f, size, size), new Vector2(0.5f, 0.5f), 100f, 0u, SpriteMeshType.FullRect, new Vector4(border, border, border, border));
			sp.name = "kk_round_" + radius;
			Cache[radius] = sp;
			return sp;
		}

		/// <summary>把 Image 变成圆角矩形（切片渲染）。</summary>
		public static void Round(Image img, int radius)
		{
			if ((Object)(object)img == (Object)null)
			{
				return;
			}
			Sprite sp = Rounded(radius);
			if ((Object)(object)sp == (Object)null)
			{
				return;
			}
			img.sprite = sp;
			img.type = Image.Type.Sliced;
		}

		/// <summary>按钮的悬停 / 按下反馈。</summary>
		public static void SetButtonFeedback(Button btn)
		{
			if ((Object)(object)btn == (Object)null)
			{
				return;
			}
			ColorBlock cb = btn.colors;
			cb.normalColor = Color.white;
			cb.highlightedColor = new Color(1.14f, 1.14f, 1.14f, 1f);
			cb.pressedColor = new Color(0.86f, 0.86f, 0.86f, 1f);
			cb.disabledColor = new Color(1f, 1f, 1f, 0.4f);
			cb.colorMultiplier = 1f;
			cb.fadeDuration = 0.06f;
			btn.colors = cb;
		}
	}
}
