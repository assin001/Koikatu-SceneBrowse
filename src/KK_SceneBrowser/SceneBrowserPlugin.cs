using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using HarmonyLib;
using Studio;
using UnityEngine;
using Object = UnityEngine.Object;
using UnityEngine.Events;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace KK_SceneBrowser;

[BepInPlugin("kk.scene.browser", "KK Scene Browser", "1.0.0")]
[BepInProcess("CharaStudio.exe")]
[DefaultExecutionOrder(10000)]
public class SceneBrowserPlugin : BaseUnityPlugin
{
	private class IdxRec
	{
		public long Size;

		public long MTicks;

		public string Ver;

		public int Obj;

		public int VerParsed;

		public float Dur;

		public int DurParsed;
	}

	private class Coll
	{
		public string Name;

		public readonly HashSet<string> Paths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
	}

	[StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
	private struct SHFILEOPSTRUCT
	{
		public IntPtr hwnd;

		public uint wFunc;

		public string pFrom;

		public string pTo;

		public ushort fFlags;

		public int fAnyOperationsAborted;

		public IntPtr hNameMappings;

		public string lpszProgressTitle;
	}

	private sealed class Entry
	{
		public string Path;

		public string Name;

		public string Dir;

		public string Author;

		public string Sub;

		public long Size;

		public DateTime MTime;

		public DateTime CTime;

		public int CDateSrc;

		public string Ver;

		public int ObjCount;

		public float Dur;

		public Texture2D Tex;

		public bool TexFailed;

		public int VerParsed;

		public int DurParsed;
	}

	private sealed class SideRow
	{
		public string Label;

		public int Count = -1;

		public string Key;

		public string Dir;

		public int Depth;

		public bool HasChildren;

		public bool Expanded;

		public UnityAction OnClick;

		public UnityAction OnToggle;
	}

	private sealed class TreeNode
	{
		public string Name;

		public string Path;

		public int Count;

		public int Direct;

		public readonly List<TreeNode> Children = new List<TreeNode>();
	}

	public const string GUID = "kk.scene.browser";

	public const string Name = "KK Scene Browser";

	public const string Version = "1.0.0";

	internal const string XUnityIgnore = "\u180e";

	private const string XUnityMark = "<color=#FFFFFF00><size=1>\u180e</size></color>";

	internal static SceneBrowserPlugin Instance;

	internal static ManualLogSource Log;

	private ConfigEntry<bool> _enabled;

	private ConfigEntry<bool> _recursive;

	private ConfigEntry<float> _thumbBudgetMs;

	private ConfigEntry<int> _colCountCfg;

	private ConfigEntry<bool> _hideBfCfg;

	private ConfigEntry<bool> _hideNativeCfg;

	private ConfigEntry<bool> _saveToAuthor;

	private static Component _panel;

	private static FieldInfo _fiListPath;

	private static FieldInfo _fiSelect;

	private static FieldInfo _fiThumbnailNum;

	private static MethodInfo _miOnClickLoad;

	private static MethodInfo _miOnClickImport;

	private static MethodInfo _miOnClickClose;

	private static FieldInfo _fiCanvasGroup;

	private static FieldInfo _fiCanvasWork;

	private static bool _initPatched;

	private float _nextPanelScan;

	private static bool _loggedSceneTypes;

	private static bool _panelAlphaArmed;

	private GameObject _canvasGo;

	private Canvas _canvas;

	private RectTransform _canvasRect;

	private GameObject _rootGo;

	private RectTransform _rootRect;

	private Font _font;

	private bool _uiBuilt;

	private bool _fontTried;

	private RectTransform _contentRect;

	private RectTransform _viewportRect;

	private Text _countLabel;

	private Text _selLabel;

	private Text _pathLabel;

	private Text _statusLabel;

	private GameObject _confirmGo;

	private readonly List<Canvas> _hiddenCanvases = new List<Canvas>();

	private int _hideFrame = -999;

	private bool _bfPatched;

	private bool _bfLogged;

	private float _bfNextTry;

	private int _bfTries;

	private Button _sortBtn;

	private Text _sortBtnText;

	private Text _orderBtnText;

	private RectTransform _sliderTrack;

	private RectTransform _sliderFill;

	private RectTransform _sliderKnob;

	private float _sliderW = 132f;

	private readonly List<Button> _chipBtns = new List<Button>();

	private readonly List<Text> _chipTexts = new List<Text>();

	private readonly List<float> _chipX = new List<float>();

	private int _chip;

	private float _chipY;

	private int _verMode;

	private int _sizeMode;

	private int _durMode;

	private float _durMin;

	private float _durMax;

	private readonly Dictionary<string, IdxRec> _idx = new Dictionary<string, IdxRec>(StringComparer.OrdinalIgnoreCase);

	private static readonly string[] VerNames = new string[3] { "全部", "KKS", "KK" };

	private static readonly string[] SizeNames = new string[5] { "全部", "< 5MB", "5–20MB", "20–50MB", "> 50MB" };

	private static readonly string[] DurNames = new string[6] { "全部", "< 10 秒", "10–15 秒", "15–30 秒", "30–60 秒", "> 60 秒" };

	private GameObject _dropGo;

	private int _dropOwner = -1;

	private int _dropFrame = -999;

	private float _sortCx;

	private float _sortY;

	private readonly List<Button> _dropBtns = new List<Button>();

	private readonly List<Text> _dropTexts = new List<Text>();

	private readonly List<Coll> _colls = new List<Coll>();

	private string _collFilter;

	private GameObject _pickGo;

	private int _pickFrame = -999;

	private string _pickTarget;

	private string _pickMoveFrom;

	private GameObject _inputGo;

	private InputField _inputField;

	private bool _inputDone;

	private UnityAction<string> _inputCb;

	private string _author;

	private string _sub;

	private readonly HashSet<string> _expanded = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

	private Text _sideHead;

	private RectTransform _sideContent;

	private RectTransform _sideViewport;

	private float _sideContentH;

	private float _sideViewH;

	private float _sideScrollY;

	private bool _sideDirty = true;

	private string _sideClickKey;

	private float _sideClickTime = -10f;

	private readonly List<Button> _sideRows = new List<Button>();

	private readonly List<Text> _sideRowTexts = new List<Text>();

	private readonly List<Text> _sideRowCounts = new List<Text>();

	private readonly List<Image> _sideSelBgs = new List<Image>();

	private readonly List<string> _sideKeys = new List<string>();

	private GameObject _detailGo;

	private Text _detailEmpty;

	private GameObject _detailModalGo;

	private RawImage _detailThumb;

	private Text _detailName;

	private Text _detailFile;

	private RectTransform _detailBadge;

	private readonly List<GameObject> _batchGo = new List<GameObject>();

	private Text _detailBadgeText;

	private Text _detailNote;

	private readonly List<Text> _detailVals = new List<Text>();

	private Text _detailFavText;

	private string _detailTexPath;

	private RectTransform _sbTrack;

	private RectTransform _sbThumb;

	private bool _batchMode;

	private bool _loading;

	private float _loadStart;

	private GameObject _loadGo;

	private Image[] _loadSpin;

	private Text _loadText;

	private GameObject _menuGo;

	private const uint FO_DELETE = 3u;

	private const ushort FOF_SILENT = 4;

	private const ushort FOF_NOCONFIRMATION = 16;

	private const ushort FOF_ALLOWUNDO = 64;

	private const ushort FOF_NOERRORUI = 1024;

	private readonly HashSet<string> _favs = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

	private readonly List<string> _recent = new List<string>();

	private readonly List<Entry> _texLru = new List<Entry>();

	private bool _thumbLogged;

	private const int TexLruMax = 260;

	private readonly HashSet<string> _multi = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

	private int _anchorIdx = -1;

	private DateTime _rootMTime = DateTime.MinValue;

	private int _rootFileCount = -1;

	private string _searchText = "";

	private RectTransform _searchBox;

	private InputField _searchField;

	private GameObject _searchGo;

	private bool _searchByAuthor;

	private Button _searchModeBtn;

	private Text _searchPlaceholder;

	private string _lastAuthor;

	private string _lastSub;

	private bool _lastPosApplied;

	private bool _wasVisible;

	private GameObject _pageGo;

	private int _pageCur;

	private int _pageCount = 1;

	private int _pageRows = 1;

	private Button _pagePrev;

	private Button _pageNext;

	private readonly List<Button> _pageBtns = new List<Button>();

	private readonly List<Text> _pageTexts = new List<Text>();

	private readonly List<int> _pageItems = new List<int>();

	private string _pageSig;

	private const int PageSlots = 8;

	private const float PageBtnW = 22f;

	private const float PageBtnPitch = 25f;

	private const float PageArrowW = 22f;

	private const float PageBtnH = 30f;

	private const float PagerW = 249f;

	private bool _xuaTried;

	private bool _xuaPatched;

	private bool _xuaGaveUp;

	private Text _detailPath;

	private Text _detailSameDirText;

	private readonly List<Entry> _all = new List<Entry>();

	private readonly List<Entry> _view = new List<Entry>();

	private readonly Dictionary<int, GameObject> _cells = new Dictionary<int, GameObject>();

	private int _selected = -1;

	private bool _scanRunning;

	private int _scanDone;

	private int _lastScanDone = -1;

	private bool _needsRefresh;

	private bool _scanComplete;

	private int _dirCount;

	private static string _lastLoadedDir;

	private static string _lastLoadedAuthor;

	private static string _lastLoadedSub;

	private static string _lastScenePath;

	private static string _overwritePath;

	private bool _overwriteBtnTried;

	private bool _overwriteScanLogged;

	private string _saveTargetKey;

	private string _saveTargetText;

	private int _sortMode = 1;

	private bool _sortDesc = true;

	private float _scrollY;

	private float _contentH;

	private const float PanelW = 1512f;

	private const float PanelRight = PanelW / 2f - 1f;

	private const float PanelLeft = 0f - PanelRight;

	private const float SideW = 208f;

	private const float SideCx = PanelLeft + 14f + SideW / 2f;

	private const float GridLeft = -515f;

	private const float GridRight = PanelRight - 16f;

	private const float ViewW = 1184f;

	private const float ViewRight = GridLeft + ViewW;

	private const float ViewX = (GridLeft + ViewRight) / 2f;

	private const float ViewH = 678f;

	private const float ViewTop = 318f;

	private const float ScrollX = 683f;

	private const float PagerX = 719f;

	private float _viewH = 600f;

	private float _sbX;

	private int _cols = 4;

	private int _colCount = 4;

	private float _cellW = 250f;

	private float _cellH = 190f;

	private float _gridOffX;

	private const float Gap = 12f;

	private const float InfoH = 46f;

	private const float PadTop = 8f;

	private const float PanelH = 980f;

	private const float PadL = 28f;

	private const float SideX = -708f;

	private const float SideTopY = 414f;

	private const float SideBotY = -378f;

	private const float SideH = 878f;

	private const float SideCY = 18f;

	private const float MainLeft = -590f;

	private const float DetailW = 300f;

	private const float DetailX = 662f;

	private const float SbW = 8f;

	private const float GridW = 1076f;

	private const float GridX = -52f;

	private const float VpTopY = 318f;

	private const float VpBotY = -360f;

	private const float KnobD = 16f;

	private static readonly Color ColBg = new Color(0f, 0f, 0f, 0.32f);

	private static readonly Color ColPanel = UiTheme.Page;

	private static readonly Color ColCard = UiTheme.Surface;

	private static readonly Color ColCardSel = UiTheme.SurfaceAlt;

	private static readonly Color ColBorder = UiTheme.Border;

	private static readonly Color ColLine = UiTheme.BorderSoft;

	private static readonly Color ColSurface = UiTheme.Surface;

	private static readonly Color ColSunken = UiTheme.Sunken;

	private static readonly Color ColAccentLine = UiTheme.AccentLine;

	private static readonly Color ColAccent = UiTheme.Accent;

	private static readonly Color ColAccentSoft = UiTheme.AccentSoft;

	private static readonly Color ColGreen = UiTheme.Green;

	private static readonly Color ColDanger = UiTheme.Danger;

	private static readonly Color ColText = UiTheme.Text;

	private static readonly Color ColSub = UiTheme.TextSub;

	private static readonly Color ColDim = UiTheme.TextDim;

	private static readonly Color ColKks = UiTheme.Green;

	private static readonly Color ColKk = new Color(0.878f, 0.565f, 0.227f, 1f);

	private static readonly Color ColField = UiTheme.Field;

	private static readonly Color ColCardText = UiTheme.TextSub;

	private static readonly Color ColDetailBg = UiTheme.Surface;

	private static readonly Color ColSideBg = UiTheme.Surface;

	private static readonly Color ColSideSel = UiTheme.Hover;

	private static readonly Color ColSideRow = UiTheme.TextSub;

	private static readonly Color ColGold = UiTheme.Gold;

	private static readonly Color MultiCardCol = new Color32(0x1F, 0x33, 0x45, 0xFF);

	private static readonly Color ColMulti = UiTheme.Hover;

	private const int RPanel = UiTheme.RadiusPanel;

	private const int RCard = UiTheme.RadiusCard;

	private const int RItem = UiTheme.RadiusItem;

	private const int RChip = UiTheme.RadiusChip;

	private const string KeyAll = "A:";

	private const string KeyRoot = "A:\u0001";

	private static readonly byte[] DurKey = Encoding.ASCII.GetBytes("duration=\"");

	private const int DurScanMax = 16777216;

	private const int DurChunk = 524288;

	private static Texture2D _phTex;

	[DllImport("shell32.dll", CharSet = CharSet.Unicode)]
	private static extern int SHFileOperation(ref SHFILEOPSTRUCT lpFileOp);

	private static bool MoveToRecycleBin(string path)
	{
		try
		{
			SHFILEOPSTRUCT lpFileOp = default(SHFILEOPSTRUCT);
			lpFileOp.wFunc = 3u;
			lpFileOp.pFrom = path + "\0\0";
			lpFileOp.fFlags = 1108;
			return SHFileOperation(ref lpFileOp) == 0 && lpFileOp.fAnyOperationsAborted == 0 && !File.Exists(path);
		}
		catch (Exception)
		{
			return false;
		}
	}

	private void Awake()
	{
		Instance = this;
		Log = Logger;
		PngAssist.OnError = delegate(string msg)
		{
			Log.LogWarning((object)("png: " + msg));
		};
		try
		{
			_enabled = ((BaseUnityPlugin)this).Config.Bind<bool>("General", "Enabled", true, "启用场景卡浏览器");
			_recursive = ((BaseUnityPlugin)this).Config.Bind<bool>("General", "Recursive scan", true, "递归扫描 studio/scene 下所有子目录（原生只扫根目录）");
			_thumbBudgetMs = ((BaseUnityPlugin)this).Config.Bind<float>("Performance", "Thumbnail budget per frame (ms)", 6f, "每帧最多花多少毫秒解码缩略图");
			_colCountCfg = ((BaseUnityPlugin)this).Config.Bind<int>("UI", "Grid columns", 4, new ConfigDescription("每行显示几张卡（4~10）", (AcceptableValueBase)(object)new AcceptableValueRange<int>(4, 10), new object[0]));
			_colCount = Mathf.Clamp(_colCountCfg.Value, 4, 10);
			_hideBfCfg = ((BaseUnityPlugin)this).Config.Bind<bool>("UI", "Hide BrowserFolders bottom menu", true, "打开本面板时临时隐藏 BrowserFolders 的底部按钮条，关闭后恢复");
			_hideNativeCfg = ((BaseUnityPlugin)this).Config.Bind<bool>("UI", "Hide native scene panel", true, "打开本面板时隐藏原生场景卡面板的绘制（原生对象仍激活，读取/导入流程不变）");
			_saveToAuthor = ((BaseUnityPlugin)this).Config.Bind<bool>("Save", "Save scene to current author folder", true, "保存场景卡时存进「最后读取那张卡所在的作者目录」，而不是 scene 根目录。关闭则回到原生行为（一律存 scene 根）");
		}
		catch (Exception ex)
		{
			Log.LogError((object)("config: " + ex));
		}
		try
		{
			BindPanelRuntime(typeof(SceneLoadScene));
			Log.LogInfo((object)("bind listPath=" + ((object)_fiListPath != null) + " select=" + ((object)_fiSelect != null) + " thumb=" + ((object)_fiThumbnailNum != null) + " load=" + ((object)_miOnClickLoad != null) + " import=" + ((object)_miOnClickImport != null) + " close=" + ((object)_miOnClickClose != null) + " canvasGroup=" + ((object)_fiCanvasGroup != null)));
		}
		catch (Exception ex2)
		{
			Log.LogError((object)("reflection bind: " + ex2));
		}
		try
		{
			LogTypeDiag(typeof(SceneLoadScene));
			PatchInitInfo(typeof(SceneLoadScene), "Studio.SceneLoadScene");
			if (!_initPatched)
			{
				try
				{
					Assembly[] asms = AppDomain.CurrentDomain.GetAssemblies();
					for (int i = 0; i < asms.Length; i++)
					{
						Type cand = null;
						try
						{
							cand = asms[i].GetType("Studio.SceneLoadScene", false);
						}
						catch (Exception)
						{
						}
						if ((object)cand != null && (object)cand != (object)typeof(SceneLoadScene))
						{
							Log.LogWarning((object)("[diag] alternate type in " + asms[i].GetName().Name));
							LogTypeDiag(cand);
							PatchInitInfo(cand, cand.Assembly.GetName().Name + ".SceneLoadScene");
						}
					}
				}
				catch (Exception)
				{
				}
			}
		}
		catch (Exception ex3)
		{
			Log.LogError((object)("harmony patch: " + ex3));
		}
		try
		{
			MethodInfo method3 = typeof(SceneInfo).GetMethod("Save", BindingFlags.Instance | BindingFlags.Public, null, new Type[1] { typeof(string) }, null);
			MethodInfo method4 = typeof(SceneBrowserPlugin).GetMethod("SceneSavePrefix", BindingFlags.Static | BindingFlags.NonPublic);
			if (method3 == null || method4 == null)
			{
				Log.LogWarning((object)("save hook missing (Save=" + (method3 != null) + ")"));
			}
			else
			{
				Harmony val2 = new Harmony("kk.scene.browser.save");
				val2.Patch((MethodBase)method3, new HarmonyMethod(method4), (HarmonyMethod)null, (HarmonyMethod)null, (HarmonyMethod)null, (HarmonyMethod)null);
				Log.LogInfo((object)"patched Studio.SceneInfo.Save(string)");
			}
		}
		catch (Exception ex4)
		{
			Log.LogError((object)("patch save: " + ex4));
		}
		try
		{
			MethodInfo method5 = typeof(SceneInfo).GetMethod("Load", BindingFlags.Instance | BindingFlags.Public, null, new Type[1] { typeof(string) }, null);
			MethodInfo method6 = typeof(SceneBrowserPlugin).GetMethod("SceneLoadPostfix", BindingFlags.Static | BindingFlags.NonPublic);
			if (method5 == null || method6 == null)
			{
				Log.LogWarning((object)("load hook missing (Load=" + (method5 != null) + ")"));
			}
			else
			{
				Harmony val3 = new Harmony("kk.scene.browser.loadrec");
				val3.Patch((MethodBase)method5, (HarmonyMethod)null, new HarmonyMethod(method6), (HarmonyMethod)null, (HarmonyMethod)null, (HarmonyMethod)null);
				Log.LogInfo((object)"patched Studio.SceneInfo.Load(string)");
			}
		}
		catch (Exception ex5)
		{
			Log.LogError((object)("patch load: " + ex5));
		}
		TryPatchBrowserFolders();
		PatchXUnity();
		LoadFavs();
		LoadColls();
		IndexLoad();
		LoadLastPos();
	}

	private void PatchXUnity()
	{
		if (_xuaPatched || _xuaGaveUp)
		{
			return;
		}
		try
		{
			Assembly[] assemblies = AppDomain.CurrentDomain.GetAssemblies();
			Type type = null;
			for (int i = 0; i < assemblies.Length && type == null; i++)
			{
				string name;
				try
				{
					name = assemblies[i].GetName().Name;
				}
				catch (Exception)
				{
					continue;
				}
				if (!(name != "XUnity.AutoTranslator.Plugin.Core"))
				{
					type = assemblies[i].GetType("XUnity.AutoTranslator.Plugin.Core.Extensions.ComponentExtensions");
				}
			}
			if (type == null)
			{
				return;
			}
			_xuaTried = true;
			MethodInfo method = type.GetMethod("ShouldIgnoreTextComponent", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
			if (method == null)
			{
				Log.LogWarning((object)"xunity: ShouldIgnoreTextComponent not found");
				_xuaGaveUp = true;
				return;
			}
			MethodInfo method2 = typeof(SceneBrowserPlugin).GetMethod("XuaIgnorePrefix", BindingFlags.Static | BindingFlags.NonPublic);
			if (method2 == null)
			{
				_xuaGaveUp = true;
				return;
			}
			Harmony val = new Harmony("kk.scene.browser.xua");
			val.Patch((MethodBase)method, new HarmonyMethod(method2), (HarmonyMethod)null, (HarmonyMethod)null, (HarmonyMethod)null, (HarmonyMethod)null);
			_xuaPatched = true;
			Log.LogInfo((object)"patched XUnity ShouldIgnoreTextComponent");
		}
		catch (Exception ex2)
		{
			_xuaTried = true;
			_xuaGaveUp = true;
			Log.LogWarning((object)("xunity patch failed (give up): " + ex2.Message));
		}
	}

	private static bool XuaIgnorePrefix(object ui, ref bool __result)
	{
		try
		{
			Component val = (Component)((ui is Component) ? ui : null);
			if ((Object)(object)val == (Object)null)
			{
				return true;
			}
			SceneBrowserPlugin instance = Instance;
			if ((Object)(object)instance == (Object)null || (Object)(object)instance._canvasGo == (Object)null)
			{
				return true;
			}
			Transform transform = instance._canvasGo.transform;
			Transform val2 = val.transform;
			int num = 0;
			while ((Object)(object)val2 != (Object)null && num++ < 64)
			{
				if ((Object)(object)val2 == (Object)(object)transform)
				{
					__result = true;
					return false;
				}
				val2 = val2.parent;
			}
		}
		catch (Exception)
		{
		}
		return true;
	}

	private void TryPatchBrowserFolders()
	{
		if (_bfPatched || _hideBfCfg == null || !_hideBfCfg.Value)
		{
			return;
		}
		if (_bfTries >= 60)
		{
			return;
		}
		if (Time.realtimeSinceStartup < _bfNextTry)
		{
			return;
		}
		_bfTries++;
		_bfNextTry = Time.realtimeSinceStartup + 1f;
		if (PatchBrowserFolders() > 0)
		{
			_bfPatched = true;
		}
	}

	private int PatchBrowserFolders()
	{
		if (_hideBfCfg == null || !_hideBfCfg.Value)
		{
			return 0;
		}
		int num = 0;
		try
		{
			MethodInfo method = typeof(SceneBrowserPlugin).GetMethod("BfOnGuiPrefix", BindingFlags.Static | BindingFlags.NonPublic);
			Assembly[] assemblies = AppDomain.CurrentDomain.GetAssemblies();
			for (int i = 0; i < assemblies.Length; i++)
			{
				Type[] types = SafeTypes(assemblies[i]);
				for (int j = 0; j < types.Length; j++)
				{
					Type type = types[j];
					if ((object)type == null || type.FullName == null)
					{
						continue;
					}
					if (type.FullName.IndexOf("<>", StringComparison.Ordinal) >= 0)
					{
						continue;
					}
					if (!type.FullName.StartsWith("BrowserFolders.", StringComparison.Ordinal) && type.Name.IndexOf("BrowserFolders", StringComparison.Ordinal) < 0)
					{
						continue;
					}
					MethodInfo method2 = FindOnGui(type);
					if ((object)method2 == null)
					{
						continue;
					}
					try
					{
						new Harmony("kk.scene.browser.bf." + type.FullName).Patch((MethodBase)method2, new HarmonyMethod(method), (HarmonyMethod)null, (HarmonyMethod)null, (HarmonyMethod)null, (HarmonyMethod)null);
						num++;
						Log.LogInfo((object)("patched " + type.FullName + ".OnGUI"));
					}
					catch (Exception ex)
					{
						Log.LogWarning((object)("patch bf " + type.FullName + ": " + ex.Message));
					}
				}
			}
			if (num == 0 && !_bfLogged)
			{
				_bfLogged = true;
				Log.LogInfo((object)"BrowserFolders not loaded yet (will retry)");
			}
		}
		catch (Exception ex2)
		{
			Log.LogError((object)("patch BrowserFolders: " + ex2));
		}
		return num;
	}

	private static Type[] SafeTypes(Assembly asm)
	{
		Type[] array;
		try
		{
			array = asm.GetTypes();
		}
		catch (ReflectionTypeLoadException ex)
		{
			array = ex.Types;
		}
		catch (Exception)
		{
			return new Type[0];
		}
		if (array == null)
		{
			return new Type[0];
		}
		List<Type> list = new List<Type>(array.Length);
		for (int i = 0; i < array.Length; i++)
		{
			if ((object)array[i] != null)
			{
				list.Add(array[i]);
			}
		}
		return list.ToArray();
	}

	private static MethodInfo FindOnGui(Type type)
	{
		for (Type type2 = type; (object)type2 != null && type2 != typeof(object); type2 = type2.BaseType)
		{
			MethodInfo method = type2.GetMethod("OnGUI", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly);
			if ((object)method != null && !method.IsStatic)
			{
				return method;
			}
		}
		return null;
	}

	private static bool BfOnGuiPrefix()
	{
		try
		{
			SceneBrowserPlugin instance = Instance;
			if ((Object)(object)instance == (Object)null)
			{
				return true;
			}
			if (instance._enabled != null && !instance._enabled.Value)
			{
				return true;
			}
			if ((Object)(object)_panel == (Object)null)
			{
				return true;
			}
			return false;
		}
		catch (Exception)
		{
			return true;
		}
	}

	private static void InitInfoPost(SceneLoadScene __instance)
	{
		try
		{
			if ((Object)(object)__instance == (Object)null)
			{
				return;
			}
			bool flag = (Object)(object)_panel == (Object)null || _panel != __instance;
			_panel = __instance;
			if (flag)
			{
				_panelAlphaArmed = false;
			}
			if ((Object)(object)Instance != (Object)null && Instance._enabled != null && Instance._enabled.Value)
			{
				Instance._needsRefresh = true;
				if (flag)
				{
					Log.LogInfo((object)"scene panel opened");
				}
			}
		}
		catch (Exception ex)
		{
			if (Log != null)
			{
				Log.LogError((object)("InitInfoPost: " + ex));
			}
		}
	}

	private static MethodInfo FindInitInfo(Type t)
	{
		if ((object)t == null)
		{
			return null;
		}
		try
		{
			MethodInfo val = t.GetMethod("InitInfo", BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
			if ((object)val != null)
			{
				return val;
			}
		}
		catch (Exception)
		{
		}
		try
		{
			MethodInfo[] methods = t.GetMethods(BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
			for (int i = 0; i < methods.Length; i++)
			{
				if (methods[i].Name == "InitInfo")
				{
					return methods[i];
				}
			}
		}
		catch (Exception)
		{
		}
		return null;
	}

	private void PatchInitInfo(Type t, string tag)
	{
		if (_initPatched || (object)t == null)
		{
			return;
		}
		if ((object)t != (object)typeof(SceneLoadScene))
		{
			Log.LogWarning((object)("skip InitInfo patch on " + tag + ": type identity differs, polling fallback stays active"));
			return;
		}
		try
		{
			MethodInfo method = FindInitInfo(t);
			MethodInfo method2 = typeof(SceneBrowserPlugin).GetMethod("InitInfoPost", BindingFlags.Static | BindingFlags.NonPublic);
			if ((object)method == null || (object)method2 == null)
			{
				if ((object)method == null)
				{
					string text = ((object)t == null) ? "null" : t.FullName;
					Log.LogWarning((object)("InitInfo not found on " + text));
				}
				return;
			}
			Harmony val = new Harmony("kk.scene.browser.init");
			val.Patch((MethodBase)method, (HarmonyMethod)null, new HarmonyMethod(method2), (HarmonyMethod)null, (HarmonyMethod)null, (HarmonyMethod)null);
			_initPatched = true;
			Log.LogInfo((object)("patched " + tag + ".InitInfo"));
		}
		catch (Exception ex)
		{
			Log.LogError((object)("patch InitInfo: " + ex));
		}
	}

	private void LogTypeDiag(Type t)
	{
		try
		{
			if ((object)t == null)
			{
				Log.LogWarning((object)"[diag] type is null");
				return;
			}
			string text = "?";
			try
			{
				text = t.Assembly.Location;
			}
			catch (Exception)
			{
			}
			Log.LogWarning((object)("[diag] type=" + t.FullName + " asm=" + t.Assembly.FullName + " loc=" + text));
			MethodInfo[] methods = t.GetMethods(BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly);
			StringBuilder stringBuilder = new StringBuilder();
			for (int i = 0; i < methods.Length; i++)
			{
				if (methods[i].Name.IndexOf("Init", StringComparison.OrdinalIgnoreCase) >= 0)
				{
					stringBuilder.Append(methods[i].Name).Append(' ');
				}
			}
			Log.LogWarning((object)("[diag] methodCount=" + methods.Length + " initLike=" + ((stringBuilder.Length == 0) ? "-" : stringBuilder.ToString())));
			bool flag = (object)t.GetField("listPath", BindingFlags.Instance | BindingFlags.NonPublic) != null;
			bool flag2 = (object)t.GetField("select", BindingFlags.Instance | BindingFlags.NonPublic) != null;
			bool flag3 = (object)t.GetField("canvasGroupWork", BindingFlags.Instance | BindingFlags.NonPublic) != null;
			Log.LogWarning((object)("[diag] listPath=" + flag + " select=" + flag2 + " canvasGroupWork=" + flag3));
		}
		catch (Exception ex)
		{
			Log.LogWarning((object)("[diag] failed: " + ex.Message));
		}
	}

	private void BindPanelRuntime(Type t)
	{
		try
		{
			if ((object)t == null)
			{
				return;
			}
			BindingFlags all = BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;
			if ((object)_fiListPath == null)
			{
				_fiListPath = t.GetField("listPath", all);
			}
			if ((object)_fiSelect == null)
			{
				_fiSelect = t.GetField("select", all);
			}
			if ((object)_fiThumbnailNum == null)
			{
				_fiThumbnailNum = t.GetField("thumbnailNum", all);
			}
			if ((object)_miOnClickLoad == null)
			{
				_miOnClickLoad = t.GetMethod("OnClickLoad", all);
			}
			if ((object)_miOnClickImport == null)
			{
				_miOnClickImport = t.GetMethod("OnClickImport", all);
			}
			if ((object)_miOnClickClose == null)
			{
				_miOnClickClose = t.GetMethod("OnClickClose", all);
			}
			if ((object)_fiCanvasGroup == null)
			{
				_fiCanvasGroup = t.GetField("canvasGroupWork", all);
			}
			if ((object)_fiCanvasWork == null)
			{
				_fiCanvasWork = t.GetField("canvasWork", all);
			}
		}
		catch (Exception ex)
		{
			Log.LogError((object)("BindPanelRuntime: " + ex));
		}
	}

	private void ScanForPanel()
	{
		if (Time.realtimeSinceStartup < _nextPanelScan)
		{
			return;
		}
		_nextPanelScan = Time.realtimeSinceStartup + 0.5f;
		try
		{
			Component val = null;
			Scene val2 = UnityEngine.SceneManagement.SceneManager.GetSceneByName("StudioSceneLoad");
			if (val2.IsValid() && val2.isLoaded)
			{
				GameObject[] rootGameObjects = val2.GetRootGameObjects();
				for (int i = 0; i < rootGameObjects.Length; i++)
				{
					if ((Object)(object)rootGameObjects[i] == (Object)null || (Object)(object)val != (Object)null)
					{
						continue;
					}
					SceneLoadScene val3 = rootGameObjects[i].GetComponentInChildren<SceneLoadScene>(true);
					if ((Object)(object)val3 != (Object)null)
					{
						val = (Component)(object)val3;
						break;
					}
					MonoBehaviour[] componentsInChildren = rootGameObjects[i].GetComponentsInChildren<MonoBehaviour>(true);
					for (int j = 0; j < componentsInChildren.Length; j++)
					{
						if ((Object)(object)componentsInChildren[j] == (Object)null)
						{
							continue;
						}
						string fullName = ((object)componentsInChildren[j]).GetType().FullName;
						if (fullName != null && fullName.EndsWith(".SceneLoadScene", StringComparison.Ordinal))
						{
							val = (Component)(object)componentsInChildren[j];
							break;
						}
					}
				}
				if ((Object)(object)val == (Object)null)
				{
					LogSceneTypes(rootGameObjects);
				}
			}
			if ((Object)(object)val != (Object)null)
			{
				Log.LogInfo((object)"scene panel discovered by polling");
				_panel = val;
				_panelAlphaArmed = false;
				_needsRefresh = true;
				BindPanelRuntime(((object)val).GetType());
				PatchInitInfo(((object)val).GetType(), "SceneLoadScene(runtime)");
			}
		}
		catch (Exception ex)
		{
			Log.LogError((object)("ScanForPanel: " + ex));
		}
	}

	private void LogSceneTypes(GameObject[] roots)
	{
		try
		{
			if (_loggedSceneTypes)
			{
				return;
			}
			_loggedSceneTypes = true;
			StringBuilder stringBuilder = new StringBuilder();
			for (int i = 0; i < roots.Length; i++)
			{
				if ((Object)(object)roots[i] == (Object)null)
				{
					continue;
				}
				MonoBehaviour[] componentsInChildren = roots[i].GetComponentsInChildren<MonoBehaviour>(true);
				for (int j = 0; j < componentsInChildren.Length; j++)
				{
					if (!((Object)(object)componentsInChildren[j] == (Object)null))
					{
						string typeName = ((object)componentsInChildren[j]).GetType().FullName;
						if (typeName.IndexOf("Scene", StringComparison.OrdinalIgnoreCase) >= 0)
						{
							stringBuilder.Append(typeName).Append(" | ");
						}
					}
				}
			}
			Log.LogWarning((object)("[diag] StudioSceneLoad components: " + stringBuilder.ToString()));
		}
		catch (Exception ex)
		{
			Log.LogWarning((object)("[diag] scene types failed: " + ex.Message));
		}
	}

	private void Update()
	{
		try
		{
			if (_enabled == null || !_enabled.Value)
			{
				return;
			}
			TryPatchBrowserFolders();
			if (_loading)
			{
				AnimateLoading();
				float loadElapsed = Time.realtimeSinceStartup - _loadStart;
				bool panelGone = (Object)(object)_panel == (Object)null || (Object)(object)((Component)_panel).gameObject == (Object)null;
				if (loadElapsed > 30f || (!panelGone && loadElapsed > 2f))
				{
					ShowLoading(false);
				}
				else if (panelGone)
				{
					return;
				}
			}
			if ((Object)(object)_panel == (Object)null)
			{
				ScanForPanel();
			}
			if (!_xuaPatched && !_xuaGaveUp && !_xuaTried)
			{
				PatchXUnity();
			}
			TryBuildOverwriteButton();
			if ((Object)(object)_panel == (Object)null || (Object)(object)((Component)_panel).gameObject == (Object)null)
			{
				HideRoot();
				return;
			}
			if (!IsPanelVisible())
			{
				if (_wasVisible)
				{
					_wasVisible = false;
					SaveLastPos();
				}
				HideRoot();
				HideNativeVisuals();
				return;
			}
			_wasVisible = true;
			EnsureUi();
			ShowRoot();
			if (_needsRefresh)
			{
				_needsRefresh = false;
				ApplyLastPos();
				if (_scanComplete && _all.Count > 0)
				{
					if (RootChanged(SceneRoot()))
					{
						StartScan();
					}
					else
					{
						RefreshView(keepPos: true);
					}
				}
				else
				{
					StartScan();
				}
			}
			if (_view.Count == 0 && AllCount() == 0 && !_scanRunning && !_scanComplete)
			{
				StartScan();
			}
			PumpThumbnails();
			UpdateScrollAndCells();
			if (_scanDone != _lastScanDone)
			{
				_lastScanDone = _scanDone;
				List<int> list = new List<int>(_cells.Keys);
				for (int i = 0; i < list.Count; i++)
				{
					LayoutCell(list[i]);
				}
				RefreshDetail();
			}
			if (_sideDirty)
			{
				_sideDirty = false;
				RebuildSideList();
			}
			HandleKeys();
			if (Input.GetMouseButtonDown(0) && (Object)(object)_dropGo != (Object)null && Time.frameCount != _dropFrame)
			{
				Vector2 mp = default(Vector2);
				mp = new Vector2(Input.mousePosition.x, Input.mousePosition.y);
				if (!MouseInRect(_dropGo) && !MouseInAnyDropTrigger(mp))
				{
					CloseDrop();
				}
			}
			if (Input.GetMouseButtonDown(0) && (Object)(object)_inputGo != (Object)null && !MouseInRect(_inputGo))
			{
				CloseInput();
			}
			if (Input.GetMouseButtonDown(0) && (Object)(object)_pickGo != (Object)null && (Object)(object)_inputGo == (Object)null && Time.frameCount != _pickFrame && !MouseInRect(_pickGo))
			{
				CloseCollPicker();
			}
			if (Input.GetMouseButtonDown(0) && (Object)(object)_menuGo != (Object)null)
			{
				RectTransform component = _menuGo.GetComponent<RectTransform>();
				Vector2 val = default(Vector2);
				val = new Vector2(Input.mousePosition.x, Input.mousePosition.y);
				if ((Object)(object)component == (Object)null || !RectTransformUtility.RectangleContainsScreenPoint(component, val, (Camera)null))
				{
					CloseContextMenu();
				}
			}
			if ((Object)(object)_searchBox != (Object)null)
			{
				Image searchImg = ((Component)_searchBox).GetComponent<Image>();
				if ((Object)(object)searchImg != (Object)null)
				{
					bool focused = (Object)(object)_searchField != (Object)null && _searchField.isFocused;
					((Graphic)searchImg).color = (Color)(focused ? ColAccentLine : ColBorder);
				}
			}
			if ((Object)(object)_countLabel != (Object)null)
			{
				string text = "<color=#FFFFFF00><size=1>\u180e</size></color>" + _view.Count + " / " + AllCount() + " 张";
				if (_countLabel.text != text)
				{
					_countLabel.text = text;
				}
			}
			if ((Object)(object)_statusLabel != (Object)null)
			{
				string text2 = ((_searchText.Length > 0) ? ("搜索「" + _searchText + "」  " + _view.Count + " 张  ·  Esc 关闭") : ((!_scanRunning) ? (_scanComplete ? ("已索引 " + AllCount() + " 张") : "") : ("索引中  " + _scanDone + " / " + AllCount())));
				string text3 = "<color=#FFFFFF00><size=1>\u180e</size></color>" + text2;
				if (_statusLabel.text != text3)
				{
					_statusLabel.text = text3;
				}
			}
			if (!((Object)(object)_pathLabel != (Object)null))
			{
				return;
			}
			StringBuilder stringBuilder = new StringBuilder();
			stringBuilder.Append("UserData / studio / scene");
			if (_author != null)
			{
				stringBuilder.Append(" / ").Append((_author.Length == 0) ? "未分类" : _author);
				if (!string.IsNullOrEmpty(_sub))
				{
					stringBuilder.Append(" / ").Append(_sub);
				}
			}
			stringBuilder.Append("   ·   ");
			if (_dirCount > 0)
			{
				stringBuilder.Append(_dirCount).Append(" 个目录   ·   ");
			}
			stringBuilder.Append(AllCount()).Append(" 张   ·   ").Append(FormatSize(TotalSize()));
			if (_saveToAuthor != null && _saveToAuthor.Value)
			{
				string text4 = SaveTargetText(SceneRoot());
				if (text4.Length > 0)
				{
					stringBuilder.Append("   ·   存卡 → ").Append(text4);
				}
			}
			string text5 = "<color=#FFFFFF00><size=1>\u180e</size></color>" + stringBuilder.ToString();
			if (_pathLabel.text != text5)
			{
				_pathLabel.text = text5;
			}
		}
		catch (Exception ex)
		{
			Log.LogError((object)("Update: " + ex));
			_panel = null;
		}
	}

	private void HandleKeys()
	{
		if ((Object)(object)_detailModalGo != (Object)null)
		{
			if (Input.GetKeyDown((KeyCode)27))
			{
				CloseDetailModal();
			}
			return;
		}
		if ((Object)(object)_confirmGo != (Object)null)
		{
			if (Input.GetKeyDown((KeyCode)27))
			{
				Object.Destroy((Object)(object)_confirmGo);
				_confirmGo = null;
			}
			return;
		}
		if ((Object)(object)_inputGo != (Object)null)
		{
			if (Input.GetKeyDown((KeyCode)27))
			{
				CloseInput();
			}
			return;
		}
		if ((Object)(object)_searchField != (Object)null && _searchField.isFocused)
		{
			HandleSearchKeys();
			return;
		}
		if (Input.GetKeyDown((KeyCode)27))
		{
			if ((Object)(object)_pickGo != (Object)null)
			{
				CloseCollPicker();
			}
			else if (_batchMode)
			{
				ExitBatchMode();
			}
			else if ((Object)(object)_dropGo != (Object)null)
			{
				CloseDrop();
			}
			else if ((Object)(object)_menuGo != (Object)null)
			{
				CloseContextMenu();
			}
			else if (_searchText.Length > 0)
			{
				_searchText = "";
				if ((Object)(object)_searchField != (Object)null)
				{
					_searchField.text = "";
				}
				RefreshView(keepPos: true);
			}
			else
			{
				OnClickClose();
			}
		}
		else if (Input.GetKeyDown((KeyCode)13) || Input.GetKeyDown((KeyCode)271))
		{
			OnClickLoad();
		}
		else if (Input.GetKeyDown((KeyCode)276))
		{
			MoveSel(-1);
		}
		else if (Input.GetKeyDown((KeyCode)275))
		{
			MoveSel(1);
		}
		else if (Input.GetKeyDown((KeyCode)273))
		{
			MoveSel(-_cols);
		}
		else if (Input.GetKeyDown((KeyCode)274))
		{
			MoveSel(_cols);
		}
		else if (Input.GetKeyDown((KeyCode)280))
		{
			MoveSel(-_cols * 3);
		}
		else if (Input.GetKeyDown((KeyCode)281))
		{
			MoveSel(_cols * 3);
		}
		else if (Input.GetKeyDown((KeyCode)278))
		{
			MoveSel(-999999);
		}
		else if (Input.GetKeyDown((KeyCode)279))
		{
			MoveSel(999999);
		}
	}

	private void MoveSel(int delta)
	{
		if (_view.Count != 0)
		{
			int num = ((_selected >= 0) ? (_selected + delta) : 0);
			num = Mathf.Clamp(num, 0, _view.Count - 1);
			SelectIndex(num);
			EnsureVisible(num);
		}
	}

	private void EnsureVisible(int idx)
	{
		if (idx >= 0 && idx < _view.Count)
		{
			float num = _cellH + 12f;
			int num2 = idx / _cols;
			float num3 = 8f + (float)num2 * num;
			float num4 = num3 + _cellH;
			if (num3 < _scrollY)
			{
				_scrollY = num3;
			}
			else if (num4 > _scrollY + _viewH)
			{
				_scrollY = num4 - _viewH;
			}
			UpdateScrollAndCells();
		}
	}

	private void SelectAllInView()
	{
		_batchMode = true;
		_multi.Clear();
		for (int i = 0; i < _view.Count; i++)
		{
			_multi.Add(_view[i].Path);
		}
		UpdateSelectionLabel();
		RedrawCells();
		Log.LogInfo((object)("select all: " + _multi.Count));
	}

	private void SelectAllVisible()
	{
		_batchMode = true;
		_multi.Clear();
		if (_view.Count == 0)
		{
			UpdateSelectionLabel();
			RedrawCells();
			return;
		}
		float num = _cellH + 12f;
		int num2 = Mathf.Max(0, (int)Mathf.Floor(_scrollY / num));
		int num3 = Mathf.Max(1, Mathf.CeilToInt(_viewH / num));
		int num4 = num2 * _cols;
		int num5 = Mathf.Min(_view.Count - 1, (num2 + num3) * _cols - 1);
		for (int i = num4; i <= num5; i++)
		{
			_multi.Add(_view[i].Path);
		}
		UpdateSelectionLabel();
		RedrawCells();
	}

	private void FocusSearchField()
	{
		if ((Object)(object)_searchField != (Object)null)
		{
			_searchField.ActivateInputField();
		}
	}

	private void CloseSearch(bool clear)
	{
		if ((Object)(object)_searchField != (Object)null)
		{
			_searchField.DeactivateInputField();
		}
		if (clear && _searchText.Length > 0)
		{
			_searchText = "";
			if ((Object)(object)_searchField != (Object)null)
			{
				_searchField.text = "";
			}
			RefreshView(keepPos: true);
		}
	}

	private void HandleSearchKeys()
	{
		if (Input.GetKeyDown((KeyCode)27))
		{
			CloseSearch(clear: true);
		}
	}

	private static bool HasText(string s, string q)
	{
		if (string.IsNullOrEmpty(s) || string.IsNullOrEmpty(q))
		{
			return false;
		}
		return s.ToLowerInvariant().IndexOf(q.ToLowerInvariant(), StringComparison.Ordinal) >= 0;
	}

	private bool MatchesSearch(Entry e)
	{
		string text = _searchText.Trim();
		if (text.Length == 0)
		{
			return true;
		}
		if (_searchByAuthor)
		{
			return HasText(e.Author, text) || HasText(e.Dir, text) || HasText(e.Sub, text);
		}
		return HasText(e.Name, text);
	}

	private long TotalSize()
	{
		long num = 0L;
		lock (_all)
		{
			for (int i = 0; i < _all.Count; i++)
			{
				num += _all[i].Size;
			}
			return num;
		}
	}

	private bool IsPanelVisible()
	{
		if ((Object)(object)_panel == (Object)null)
		{
			return false;
		}
		if (OverlaySceneOpen())
		{
			return false;
		}
		GameObject gameObject;
		try
		{
			gameObject = ((Component)_panel).gameObject;
		}
		catch (Exception)
		{
			_panel = null;
			return false;
		}
		if ((Object)(object)gameObject == (Object)null)
		{
			_panel = null;
			return false;
		}
		try
		{
			Scene scene = gameObject.scene;
			if (!scene.IsValid() || !scene.isLoaded)
			{
				_panel = null;
				return false;
			}
		}
		catch (Exception)
		{
			return false;
		}
		if (!gameObject.activeInHierarchy)
		{
			return false;
		}
		try
		{
			if ((object)_fiCanvasGroup != null)
			{
				CanvasGroup obj = (CanvasGroup)_fiCanvasGroup.GetValue(_panel);
				if ((Object)(object)obj != (Object)null)
				{
					if (obj.alpha >= 0.5f)
					{
						_panelAlphaArmed = true;
					}
					else if (_panelAlphaArmed)
					{
						return false;
					}
				}
			}
		}
		catch (Exception)
		{
		}
		return true;
	}

	private void ApplyCanvasOrder()
	{
		if ((Object)(object)_canvas == (Object)null)
		{
			return;
		}
		int num = 200;
		try
		{
			if ((object)_fiCanvasWork != null && (Object)(object)_panel != (Object)null)
			{
				Canvas val = (Canvas)_fiCanvasWork.GetValue(_panel);
				if ((Object)(object)val != (Object)null)
				{
					num = val.sortingOrder + 1;
				}
			}
		}
		catch (Exception)
		{
		}
		num = Mathf.Max(100, num);
		if (_canvas.sortingOrder != num)
		{
			_canvas.sortingOrder = num;
		}
	}

	private bool OverlaySceneOpen()
	{
		try
		{
			int sceneCount = UnityEngine.SceneManagement.SceneManager.sceneCount;
			for (int i = 0; i < sceneCount; i++)
			{
				Scene scene = UnityEngine.SceneManagement.SceneManager.GetSceneAt(i);
				if (!scene.IsValid() || !scene.isLoaded)
				{
					continue;
				}
				string name = scene.name;
				if (name == "StudioCheck" || name == "StudioNotification")
				{
					return true;
				}
			}
		}
		catch (Exception)
		{
		}
		return false;
	}

	private void ShowRoot()
	{
		ApplyCanvasOrder();
		if ((Object)(object)_rootGo != (Object)null && !_rootGo.activeSelf)
		{
			_rootGo.SetActive(true);
		}
		HideNativeVisuals();
	}

	private void HideRoot()
	{
		if ((Object)(object)_rootGo != (Object)null && _rootGo.activeSelf)
		{
			SaveLastPos();
			_rootGo.SetActive(false);
		}
		if ((Object)(object)_confirmGo != (Object)null && _confirmGo.activeSelf)
		{
			_confirmGo.SetActive(false);
		}
		if ((Object)(object)_searchField != (Object)null)
		{
			CloseSearch(clear: false);
		}
		CloseDrop();
		CloseContextMenu();
		CloseCollPicker();
		CloseInput();
		CloseDetailModal();
		PruneHiddenCanvases();
	}

	private void PruneHiddenCanvases()
	{
		for (int num = _hiddenCanvases.Count - 1; num >= 0; num--)
		{
			if ((Object)(object)_hiddenCanvases[num] == (Object)null)
			{
				_hiddenCanvases.RemoveAt(num);
			}
		}
	}

	private void HideNativeVisuals()
	{
		if ((_hideNativeCfg != null && !_hideNativeCfg.Value) || (Object)(object)_panel == (Object)null || (_hiddenCanvases.Count > 0 && Time.frameCount - _hideFrame < 20))
		{
			return;
		}
		_hideFrame = Time.frameCount;
		try
		{
			Scene scene = ((Component)_panel).gameObject.scene;
			if (!scene.IsValid() || !scene.isLoaded)
			{
				return;
			}
			GameObject[] rootGameObjects = scene.GetRootGameObjects();
			for (int i = 0; i < rootGameObjects.Length; i++)
			{
				if ((Object)(object)rootGameObjects[i] == (Object)null || (Object)(object)rootGameObjects[i] == (Object)(object)_canvasGo)
				{
					continue;
				}
				Canvas[] componentsInChildren = rootGameObjects[i].GetComponentsInChildren<Canvas>(true);
				foreach (Canvas val in componentsInChildren)
				{
					if (!((Object)(object)val == (Object)null) && !((Object)(object)((Component)val).gameObject == (Object)(object)_canvasGo) && ((Behaviour)val).enabled)
					{
						((Behaviour)val).enabled = false;
						if (!_hiddenCanvases.Contains(val))
						{
							_hiddenCanvases.Add(val);
						}
					}
				}
			}
		}
		catch (Exception ex)
		{
			Log.LogError((object)("hide native: " + ex));
		}
	}

	private void RestoreNativeVisuals()
	{
		if (_hiddenCanvases.Count == 0)
		{
			return;
		}
		for (int i = 0; i < _hiddenCanvases.Count; i++)
		{
			if ((Object)(object)_hiddenCanvases[i] != (Object)null)
			{
				((Behaviour)_hiddenCanvases[i]).enabled = true;
			}
		}
		_hiddenCanvases.Clear();
	}

	private string SceneRoot()
	{
		string text;
		try
		{
			text = UserData.Create("studio/scene");
		}
		catch (Exception)
		{
			text = Application.dataPath + "/../UserData/studio/scene";
		}
		try
		{
			text = Path.GetFullPath(text);
		}
		catch (Exception)
		{
		}
		return text;
	}

	private string SaveTargetDir(string root)
	{
		bool flag = _author == _lastLoadedAuthor && _sub == _lastLoadedSub;
		string lastLoadedDir = _lastLoadedDir;
		if (flag && !string.IsNullOrEmpty(lastLoadedDir))
		{
			try
			{
				if (Directory.Exists(lastLoadedDir))
				{
					return lastLoadedDir;
				}
			}
			catch (Exception)
			{
			}
		}
		if (_author == null)
		{
			return null;
		}
		string text = root;
		if (_author.Length > 0)
		{
			text = Path.Combine(text, _author);
		}
		if (!string.IsNullOrEmpty(_sub))
		{
			text = Path.Combine(text, _sub.Replace('/', '\\'));
		}
		return text;
	}

	private string SaveTargetText(string root)
	{
		string text = ((_author == null) ? "\u0001" : _author) + "\u0002" + ((_sub == null) ? "\u0001" : _sub) + "\u0002" + ((_lastLoadedDir == null) ? "" : _lastLoadedDir);
		if (text == _saveTargetKey)
		{
			return _saveTargetText;
		}
		_saveTargetKey = text;
		string text2 = SaveTargetDir(root);
		if (string.IsNullOrEmpty(text2))
		{
			_saveTargetText = "";
			return _saveTargetText;
		}
		string text3 = root.TrimEnd('\\');
		string text4 = ((text2.Length > text3.Length) ? text2.Substring(text3.Length).TrimStart('\\') : "");
		_saveTargetText = ((text4.Length == 0) ? "scene 根" : text4);
		return _saveTargetText;
	}

	[HarmonyPriority(0)]
	private static void SceneSavePrefix(ref string _path)
	{
		try
		{
			SceneBrowserPlugin instance = Instance;
			if ((Object)(object)instance == (Object)null)
			{
				return;
			}
			if (!string.IsNullOrEmpty(_overwritePath))
			{
				string overwritePath = _overwritePath;
				_overwritePath = null;
				_path = overwritePath;
				if (Log != null)
				{
					Log.LogInfo((object)("overwrite scene -> " + overwritePath));
				}
			}
			else
			{
				if (instance._saveToAuthor == null || !instance._saveToAuthor.Value || string.IsNullOrEmpty(_path))
				{
					return;
				}
				string text = _path.Replace('/', '\\');
				if (text.IndexOf("_autosave", StringComparison.OrdinalIgnoreCase) >= 0)
				{
					return;
				}
				string fullPath;
				try
				{
					fullPath = Path.GetFullPath(text);
				}
				catch (Exception)
				{
					return;
				}
				string text2 = instance.SceneRoot();
				string text3 = text2.TrimEnd('\\');
				if (!fullPath.StartsWith(text3 + "\\", StringComparison.OrdinalIgnoreCase))
				{
					return;
				}
				string fileName = Path.GetFileName(fullPath);
				if (string.IsNullOrEmpty(fileName))
				{
					return;
				}
				string text4 = instance.SaveTargetDir(text2);
				if (string.IsNullOrEmpty(text4))
				{
					return;
				}
				string text5 = Path.Combine(text4, fileName);
				if (!string.Equals(text5, fullPath, StringComparison.OrdinalIgnoreCase))
				{
					try
					{
						Directory.CreateDirectory(text4);
					}
					catch (Exception)
					{
						return;
					}
					_path = text5;
					if (Log != null)
					{
						Log.LogInfo((object)("save scene -> " + text5));
					}
				}
			}
		}
		catch (Exception ex3)
		{
			if (Log != null)
			{
				Log.LogError((object)("save prefix: " + ex3));
			}
		}
	}

	private static void SceneLoadPostfix(string __0, bool __result)
	{
		try
		{
			SceneBrowserPlugin instance = Instance;
			if ((Object)(object)instance != (Object)null && instance._loading)
			{
				instance.ShowLoading(false);
			}
			if (__result && !string.IsNullOrEmpty(__0))
			{
				_lastScenePath = __0;
				if (Log != null)
				{
					Log.LogInfo((object)("last scene card: " + __0));
				}
			}
		}
		catch (Exception)
		{
		}
	}

	private void TryBuildOverwriteButton()
	{
		if (_overwriteBtnTried)
		{
			return;
		}
		try
		{
			Studio.Studio instance = Singleton<Studio.Studio>.Instance;
			if ((Object)(object)instance == (Object)null)
			{
				return;
			}
			SystemButtonCtrl systemButtonCtrl = instance.systemButtonCtrl;
			if ((Object)(object)systemButtonCtrl == (Object)null)
			{
				return;
			}
			Button val = FindSaveButton(((Component)systemButtonCtrl).transform);
			if ((Object)(object)val == (Object)null)
			{
				if (!_overwriteScanLogged)
				{
					_overwriteScanLogged = true;
					LogButtonTexts(((Component)systemButtonCtrl).transform);
				}
				return;
			}
			Transform parent = ((Component)val).transform.parent;
			if ((Object)(object)parent == (Object)null)
			{
				return;
			}
			Transform transform = ((Component)val).transform;
			RectTransform val2 = (RectTransform)(object)((transform is RectTransform) ? transform : null);
			if ((Object)(object)val2 == (Object)null)
			{
				return;
			}
			int siblingIndex = ((Component)val).transform.GetSiblingIndex();
			bool flag = HasLayoutGroup(parent);
			float num = 0f;
			Rect rect;
			if (!flag)
			{
				Transform val3 = ((siblingIndex + 1 < parent.childCount) ? parent.GetChild(siblingIndex + 1) : null);
				RectTransform val4 = (RectTransform)(object)((val3 is RectTransform) ? val3 : null);
				if ((Object)(object)val4 != (Object)null)
				{
					num = val2.anchoredPosition.y - val4.anchoredPosition.y;
				}
				if (Mathf.Abs(num) < 1f)
				{
					rect = val2.rect;
					num = rect.height;
				}
			}
			GameObject val5 = Object.Instantiate<GameObject>(((Component)val).gameObject, parent);
			((Object)val5).name = "Btn_OverwriteData";
			val5.transform.SetSiblingIndex(siblingIndex + 1);
			SetAllTexts(val5.transform, "覆盖数据");
			Button component = val5.GetComponent<Button>();
			if ((Object)(object)component != (Object)null)
			{
				component.onClick = new Button.ButtonClickedEvent();
				((UnityEvent)component.onClick).AddListener(new UnityAction(OnClickOverwrite));
			}
			if (!flag && Mathf.Abs(num) > 0.5f)
			{
				Transform transform2 = val5.transform;
				RectTransform val6 = (RectTransform)(object)((transform2 is RectTransform) ? transform2 : null);
				if ((Object)(object)val6 != (Object)null)
				{
					val6.anchoredPosition = new Vector2(val2.anchoredPosition.x, val2.anchoredPosition.y - num);
				}
				rect = val2.rect;
				float num2 = Mathf.Max(2f, rect.width * 0.75f);
				for (int i = siblingIndex + 2; i < parent.childCount; i++)
				{
					Transform child = parent.GetChild(i);
					RectTransform val7 = (RectTransform)(object)((child is RectTransform) ? child : null);
					if (!((Object)(object)val7 == (Object)null) && !((Object)(object)val7 == (Object)(object)val6) && !(Mathf.Abs(val7.anchoredPosition.x - val2.anchoredPosition.x) > num2))
					{
						val7.anchoredPosition = new Vector2(val7.anchoredPosition.x, val7.anchoredPosition.y - num);
					}
				}
			}
			_overwriteBtnTried = true;
			Log.LogInfo((object)("system menu: added 覆盖数据 (layout=" + flag + ", rowH=" + num.ToString("F1") + ")"));
		}
		catch (Exception ex)
		{
			_overwriteBtnTried = true;
			Log.LogError((object)("build overwrite button: " + ex));
		}
	}

	private void LogButtonTexts(Transform root)
	{
		try
		{
			Button[] componentsInChildren = ((Component)root).GetComponentsInChildren<Button>(true);
			Log.LogInfo((object)("system menu buttons: " + componentsInChildren.Length));
			for (int i = 0; i < componentsInChildren.Length && i < 40; i++)
			{
				string text = null;
				Component[] componentsInChildren2 = ((Component)componentsInChildren[i]).GetComponentsInChildren<Component>(true);
				for (int j = 0; j < componentsInChildren2.Length; j++)
				{
					string text2 = ComponentText(componentsInChildren2[j]);
					if (!string.IsNullOrEmpty(text2))
					{
						text = text2;
						break;
					}
				}
				Log.LogInfo((object)("  [" + i + "] " + ((Object)((Component)componentsInChildren[i]).gameObject).name + " = " + ((text == null) ? "(no text)" : text)));
			}
		}
		catch (Exception ex)
		{
			Log.LogWarning((object)("log buttons: " + ex.Message));
		}
	}

	private static bool HasLayoutGroup(Transform t)
	{
		if ((Object)(object)t == (Object)null)
		{
			return false;
		}
		if ((Object)(object)((Component)t).GetComponent<LayoutGroup>() != (Object)null)
		{
			return true;
		}
		if ((Object)(object)((Component)t).GetComponent<ContentSizeFitter>() != (Object)null)
		{
			return true;
		}
		return false;
	}

	private static string ComponentText(Component c)
	{
		if ((Object)(object)c == (Object)null)
		{
			return null;
		}
		Type type = ((object)c).GetType();
		if (type.Name != "TextMeshProUGUI" && type.Name != "Text")
		{
			return null;
		}
		PropertyInfo property = type.GetProperty("text", BindingFlags.Instance | BindingFlags.Public);
		if (property == null)
		{
			return null;
		}
		return property.GetValue(c, null) as string;
	}

	private static void SetAllTexts(Transform root, string txt)
	{
		if ((Object)(object)root == (Object)null)
		{
			return;
		}
		Component[] componentsInChildren = ((Component)root).GetComponentsInChildren<Component>(true);
		for (int i = 0; i < componentsInChildren.Length; i++)
		{
			if (ComponentText(componentsInChildren[i]) != null)
			{
				PropertyInfo property = ((object)componentsInChildren[i]).GetType().GetProperty("text", BindingFlags.Instance | BindingFlags.Public);
				if (property != null)
				{
					property.SetValue(componentsInChildren[i], txt, null);
				}
			}
		}
	}

	private static Button FindSaveButton(Transform root)
	{
		if ((Object)(object)root == (Object)null)
		{
			return null;
		}
		Button[] componentsInChildren = ((Component)root).GetComponentsInChildren<Button>(true);
		foreach (Button val in componentsInChildren)
		{
			if ((Object)(object)val == (Object)null || ((Object)((Component)val).gameObject).name == "Btn_OverwriteData")
			{
				continue;
			}
			string text = null;
			Component[] componentsInChildren2 = ((Component)val).GetComponentsInChildren<Component>(true);
			for (int j = 0; j < componentsInChildren2.Length; j++)
			{
				string text2 = ComponentText(componentsInChildren2[j]);
				if (!string.IsNullOrEmpty(text2))
				{
					text = text2;
					break;
				}
			}
			if (!string.IsNullOrEmpty(text) && text.IndexOf("保存", StringComparison.Ordinal) >= 0 && text.IndexOf("读取", StringComparison.Ordinal) < 0 && text.IndexOf("覆盖", StringComparison.Ordinal) < 0)
			{
				return val;
			}
		}
		return null;
	}

	private void InvalidateEntry(string path)
	{
		if (string.IsNullOrEmpty(path))
		{
			return;
		}
		Entry entry = null;
		lock (_all)
		{
			for (int i = 0; i < _all.Count; i++)
			{
				if (string.Equals(_all[i].Path, path, StringComparison.OrdinalIgnoreCase))
				{
					entry = _all[i];
					break;
				}
			}
		}
		if (entry == null)
		{
			return;
		}
		if ((Object)(object)entry.Tex != (Object)null)
		{
			try
			{
				Object.Destroy((Object)(object)entry.Tex);
			}
			catch (Exception)
			{
			}
			entry.Tex = null;
		}
		entry.TexFailed = false;
		entry.VerParsed = 0;
		entry.DurParsed = 0;
		entry.Dur = 0f;
		entry.ObjCount = -1;
		try
		{
			FileInfo fileInfo = new FileInfo(path);
			if (fileInfo.Exists)
			{
				entry.Size = fileInfo.Length;
				entry.MTime = fileInfo.LastWriteTime;
				DateTime ctime = DateTime.MinValue;
				try
				{
					ctime = fileInfo.CreationTime;
				}
				catch (Exception)
				{
				}
				entry.CTime = BestCardDate(entry.Name, ctime, entry.MTime, out entry.CDateSrc);
			}
		}
		catch (Exception)
		{
		}
		try
		{
			_idx.Remove(path);
		}
		catch (Exception)
		{
		}
		try
		{
			FileStream fileStream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite, 65536);
			try
			{
				ParseTail(entry);
			}
			finally
			{
				fileStream.Dispose();
			}
		}
		catch (Exception)
		{
		}
		try
		{
			IndexSave();
		}
		catch (Exception)
		{
		}
		if (_sortMode == 0 || _sortMode == 1)
		{
			try
			{
				RefreshView(keepPos: true);
			}
			catch (Exception)
			{
			}
			try
			{
				if (_selected >= 0)
				{
					EnsureVisible(_selected);
				}
			}
			catch (Exception)
			{
			}
		}
		else
		{
			try
			{
				LayoutCellForPath(path);
			}
			catch (Exception)
			{
			}
		}
		try
		{
			RefreshDetail();
		}
		catch (Exception)
		{
		}
		if (Log != null)
		{
			Log.LogInfo((object)("entry refreshed: " + Path.GetFileName(path) + "  size=" + entry.Size + "  mtime=" + entry.MTime.ToString("yyyy-MM-dd HH:mm:ss")));
		}
	}

	private void LayoutCellForPath(string path)
	{
		for (int i = 0; i < _view.Count; i++)
		{
			if (string.Equals(_view[i].Path, path, StringComparison.OrdinalIgnoreCase))
			{
				if (_cells.TryGetValue(i, out var value) && (Object)(object)value != (Object)null)
				{
					LayoutCell(i);
				}
				break;
			}
		}
	}

	private void OnClickOverwrite()
	{
		try
		{
			if (string.IsNullOrEmpty(_lastScenePath) || !File.Exists(_lastScenePath))
			{
				return;
			}
			string lastScenePath = _lastScenePath;
			string text = lastScenePath + ".writing";
			string text2 = lastScenePath + ".old";
			Studio.Studio instance = Singleton<Studio.Studio>.Instance;
			if ((Object)(object)instance == (Object)null)
			{
				return;
			}
			try
			{
				if (File.Exists(text))
				{
					File.Delete(text);
				}
			}
			catch (Exception)
			{
			}
			try
			{
				if (File.Exists(text2))
				{
					File.Delete(text2);
				}
			}
			catch (Exception)
			{
			}
			_overwritePath = text;
			if ((Object)(object)instance.systemButtonCtrl != (Object)null)
			{
				instance.systemButtonCtrl.OnClickSave();
			}
			else
			{
				instance.SaveScene();
			}
			_overwritePath = null;
			((MonoBehaviour)this).StartCoroutine(EnsureNotificationClosed());
			long num = 0L;
			try
			{
				if (File.Exists(text))
				{
					num = new FileInfo(text).Length;
				}
			}
			catch (Exception)
			{
			}
			if (num <= 4096)
			{
				Log.LogError((object)("overwrite: 临时文件没写出来，保留原卡不动（" + text + "）"));
				try
				{
					if (File.Exists(text))
					{
						File.Delete(text);
					}
					return;
				}
				catch (Exception)
				{
					return;
				}
			}
			try
			{
				File.Move(lastScenePath, text2);
				try
				{
					File.Move(text, lastScenePath);
				}
				catch (Exception)
				{
					try
					{
						File.Move(text2, lastScenePath);
					}
					catch (Exception)
					{
					}
					throw;
				}
				try
				{
					File.Delete(text2);
				}
				catch (Exception)
				{
				}
				try
				{
					File.SetCreationTime(lastScenePath, DateTime.Now);
				}
				catch (Exception)
				{
				}
				try
				{
					File.SetLastWriteTime(lastScenePath, DateTime.Now);
				}
				catch (Exception)
				{
				}
				Log.LogInfo((object)("overwrite done -> " + Path.GetFileName(lastScenePath) + "  (" + num + " bytes)"));
				InvalidateEntry(lastScenePath);
			}
			catch (Exception ex10)
			{
				Log.LogError((object)("overwrite rename failed: " + ex10));
			}
		}
		catch (Exception ex11)
		{
			_overwritePath = null;
			Log.LogError((object)("overwrite failed: " + ex11));
		}
	}

	private IEnumerator EnsureNotificationClosed()
	{
		yield return (object)new WaitForSecondsRealtime(2.5f);
		for (int pass = 0; pass < 3; pass++)
		{
			bool flag = false;
			int sceneCount = SceneManager.sceneCount;
			for (int i = 0; i < sceneCount; i++)
			{
				Scene sceneAt = SceneManager.GetSceneAt(i);
				if (sceneAt.IsValid() && sceneAt.isLoaded)
				{
					string name = sceneAt.name;
					if (name.IndexOf("Notification", StringComparison.OrdinalIgnoreCase) >= 0)
					{
						flag = true;
						Log.LogWarning((object)("notification scene still loaded after save, forcing unload: " + name));
						SceneManager.UnloadSceneAsync(sceneAt);
					}
				}
			}
			if (!flag)
			{
				break;
			}
			yield return (object)new WaitForSecondsRealtime(0.5f);
		}
	}

	private void SnapshotRoot(string rootFull)
	{
		try
		{
			DirectoryInfo directoryInfo = new DirectoryInfo(rootFull);
			_rootMTime = directoryInfo.LastWriteTime;
			_rootFileCount = Directory.GetFiles(rootFull, "*.png", SearchOption.AllDirectories).Length;
		}
		catch (Exception)
		{
			_rootMTime = DateTime.MinValue;
			_rootFileCount = -1;
		}
	}

	private bool RootChanged(string rootFull)
	{
		if (_rootFileCount < 0)
		{
			return true;
		}
		try
		{
			DirectoryInfo directoryInfo = new DirectoryInfo(rootFull);
			if (directoryInfo.LastWriteTime != _rootMTime)
			{
				return true;
			}
			int num = Directory.GetFiles(rootFull, "*.png", SearchOption.AllDirectories).Length;
			return num != _rootFileCount;
		}
		catch (Exception)
		{
			return true;
		}
	}

	private void ForceRescan()
	{
		if (!_scanRunning)
		{
			Log.LogInfo((object)"manual refresh");
			_idx.Clear();
			try
			{
				File.Delete(IndexFile());
			}
			catch (Exception)
			{
			}
			_scanComplete = false;
			lock (_all)
			{
				_all.Clear();
			}
			_texLru.Clear();
			_multi.Clear();
			StartScan();
		}
	}

	private void OnClickRefreshIndex()
	{
		if (_scanRunning)
		{
			return;
		}
		Log.LogInfo((object)("manual refresh: " + AllCount() + " -> rescan"));
		_multi.Clear();
		StartScan();
	}

	private void StartScan()
	{
		if (_scanRunning)
		{
			return;
		}
		_scanRunning = true;
		_scanComplete = false;
		_scanDone = 0;
		_lastScanDone = -1;
		string text = SceneRoot();
		bool flag = _recursive == null || _recursive.Value;
		Log.LogInfo((object)("scan start: " + text + "  recursive=" + flag));
		string text2 = text;
		try
		{
			text2 = Path.GetFullPath(text);
		}
		catch (Exception)
		{
		}
		List<Entry> list = new List<Entry>();
		try
		{
			SearchOption searchOption = (flag ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly);
			string[] files = Directory.GetFiles(text, "*.png", searchOption);
			foreach (string text3 in files)
			{
				Entry entry = new Entry();
				entry.Path = text3;
				entry.Name = Path.GetFileNameWithoutExtension(text3);
				entry.Dir = Path.GetFileName(Path.GetDirectoryName(text3));
				entry.Author = AuthorOf(text3, text2);
				entry.Sub = SubOf(text3, text2);
				try
				{
					FileInfo fileInfo = new FileInfo(text3);
					entry.Size = fileInfo.Length;
					entry.MTime = fileInfo.LastWriteTime;
					DateTime ctime = DateTime.MinValue;
					try
					{
						ctime = fileInfo.CreationTime;
					}
					catch (Exception)
					{
					}
					entry.CTime = BestCardDate(entry.Name, ctime, entry.MTime, out entry.CDateSrc);
				}
				catch (Exception)
				{
					entry.Size = 0L;
					entry.MTime = DateTime.MinValue;
					entry.CTime = DateTime.MinValue;
					entry.CDateSrc = 0;
				}
				entry.ObjCount = -1;
				list.Add(entry);
			}
		}
		catch (Exception ex4)
		{
			Log.LogError((object)("scan: " + ex4));
		}
		lock (_all)
		{
			_all.Clear();
			_all.AddRange(list);
		}
		_dirCount = 0;
		try
		{
			HashSet<string> hashSet = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
			for (int j = 0; j < list.Count; j++)
			{
				string directoryName = Path.GetDirectoryName(list[j].Path);
				if (!string.IsNullOrEmpty(directoryName))
				{
					hashSet.Add(directoryName);
				}
			}
			_dirCount = hashSet.Count;
		}
		catch (Exception)
		{
		}
		SnapshotRoot(text2);
		Log.LogInfo((object)("scan L0: " + list.Count + " png  dirs=" + _dirCount));
		try
		{
			HashSet<string> hashSet2 = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
			for (int k = 0; k < list.Count; k++)
			{
				hashSet2.Add(list[k].Author ?? "");
			}
			Log.LogInfo((object)("scan L0: authors=" + hashSet2.Count + "  root=" + text2));
		}
		catch (Exception)
		{
		}
		_sideDirty = true;
		RefreshView();
		Thread thread = new Thread(L1Worker);
		thread.IsBackground = true;
		thread.Name = "KK_SceneBrowser_L1";
		thread.Start();
	}

	private static DateTime BestCardDate(string name, DateTime ctime, DateTime mtime, out int src)
	{
		if (ctime != DateTime.MinValue && ctime.Year >= 2000)
		{
			src = 1;
			return ctime;
		}
		src = ((!(mtime == DateTime.MinValue)) ? 2 : 0);
		return mtime;
	}

	private static string AuthorOf(string path, string rootFull)
	{
		try
		{
			string text = RelUnder(path, rootFull);
			if (text != null)
			{
				int num = text.IndexOf('\\');
				return (num > 0) ? text.Substring(0, num) : "";
			}
			string directoryName = Path.GetDirectoryName(path);
			string directoryName2 = Path.GetDirectoryName(directoryName);
			return (directoryName2 == null) ? "" : Path.GetFileName(directoryName);
		}
		catch (Exception)
		{
			return "";
		}
	}

	private static string RelUnder(string path, string rootFull)
	{
		if (string.IsNullOrEmpty(rootFull) || string.IsNullOrEmpty(path))
		{
			return null;
		}
		string text = rootFull.Replace('/', '\\').TrimEnd('\\');
		string text2 = path.Replace('/', '\\');
		if (!text2.StartsWith(text + "\\", StringComparison.OrdinalIgnoreCase))
		{
			return null;
		}
		return text2.Substring(text.Length + 1);
	}

	private static string SubOf(string path, string rootFull)
	{
		try
		{
			string text = RelUnder(path, rootFull);
			if (text == null)
			{
				return "";
			}
			int num = text.LastIndexOf('\\');
			if (num <= 0)
			{
				return "";
			}
			string text2 = text.Substring(0, num).Replace('\\', '/');
			int num2 = text2.IndexOf('/');
			return (num2 < 0) ? "" : text2.Substring(num2 + 1);
		}
		catch (Exception)
		{
			return "";
		}
	}

	private void L1Worker()
	{
		int num = 0;
		int num2 = 0;
		try
		{
			List<Entry> list;
			lock (_all)
			{
				list = new List<Entry>(_all);
			}
			for (int i = 0; i < list.Count; i++)
			{
				Entry entry = list[i];
				if (entry.VerParsed == 0)
				{
					if (_idx.TryGetValue(entry.Path, out var value) && value.Size == entry.Size && value.MTicks == entry.MTime.Ticks)
					{
						entry.Ver = value.Ver;
						entry.ObjCount = value.Obj;
						entry.VerParsed = value.VerParsed;
						entry.Dur = value.Dur;
						entry.DurParsed = value.DurParsed;
						num++;
					}
					else
					{
						ParseTail(entry);
						num2++;
					}
					_scanDone = i + 1;
					if ((i & 7) == 0)
					{
						Thread.Sleep(0);
					}
				}
			}
		}
		catch (Exception ex)
		{
			Log.LogError((object)("L1: " + ex));
		}
		finally
		{
			_scanRunning = false;
			_scanComplete = true;
			_needsRefresh = true;
			_sideDirty = true;
			Log.LogInfo((object)("scan L1 done: " + _scanDone + "  cache hit=" + num + " miss=" + num2));
			IndexSave();
		}
	}

	private static void ParseTail(Entry e)
	{
		e.VerParsed = -1;
		try
		{
			using FileStream fileStream = new FileStream(PngAssist.LongPath(e.Path), FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
			long num = SkipPng(fileStream);
			if (num < 0)
			{
				return;
			}
			fileStream.Position = num;
			int num2 = fileStream.ReadByte();
			if (num2 < 1 || num2 > 64)
			{
				return;
			}
			byte[] array = new byte[num2];
			int i;
			int num3;
			for (i = 0; i < num2; i += num3)
			{
				num3 = fileStream.Read(array, i, num2 - i);
				if (num3 <= 0)
				{
					break;
				}
			}
			if (i != num2)
			{
				return;
			}
			for (int j = 0; j < num2; j++)
			{
				byte b = array[j];
				if (b < 32 || b > 126)
				{
					return;
				}
			}
			string @string = Encoding.ASCII.GetString(array);
			byte[] array2 = new byte[4];
			int k;
			int num4;
			for (k = 0; k < 4; k += num4)
			{
				num4 = fileStream.Read(array2, k, 4 - k);
				if (num4 <= 0)
				{
					break;
				}
			}
			if (k == 4)
			{
				e.Ver = @string;
				e.ObjCount = array2[0] | (array2[1] << 8) | (array2[2] << 16) | (array2[3] << 24);
				e.VerParsed = 1;
				ParseDuration(fileStream, e);
			}
		}
		catch (Exception)
		{
			e.VerParsed = -1;
		}
	}

	private static void ParseDuration(FileStream fs, Entry e)
	{
		e.DurParsed = -1;
		try
		{
			long length = fs.Length;
			long num = length;
			long num2 = 0L;
			byte[] array = new byte[0];
			while (num > 0 && num2 < 16777216)
			{
				int num3 = (int)Math.Min(524288L, num);
				num = (fs.Position = num - num3);
				byte[] array2 = new byte[num3 + array.Length];
				int i;
				int num5;
				for (i = 0; i < num3; i += num5)
				{
					num5 = fs.Read(array2, i, num3 - i);
					if (num5 <= 0)
					{
						break;
					}
				}
				if (i != num3)
				{
					break;
				}
				if (array.Length != 0)
				{
					Array.Copy(array, 0, array2, num3, array.Length);
				}
				int num6 = IndexOf(array2, DurKey);
				if (num6 >= 0)
				{
					int num7 = num6 + DurKey.Length;
					int j;
					for (j = num7; j < array2.Length; j++)
					{
						byte b = array2[j];
						if ((b < 48 || b > 57) && b != 46)
						{
							break;
						}
					}
					if (j > num7)
					{
						string @string = Encoding.ASCII.GetString(array2, num7, j - num7);
						if (float.TryParse(@string, NumberStyles.Float, CultureInfo.InvariantCulture, out var result) && result > 0f && result < 100000f)
						{
							e.Dur = result;
							e.DurParsed = 1;
							break;
						}
					}
				}
				int num8 = Math.Min(DurKey.Length - 1, array2.Length);
				array = new byte[num8];
				Array.Copy(array2, array2.Length - num8, array, 0, num8);
				num2 += num3;
			}
		}
		catch (Exception)
		{
			e.DurParsed = -1;
		}
	}

	private static int IndexOf(byte[] hay, byte[] needle)
	{
		int num = hay.Length - needle.Length;
		for (int i = 0; i <= num; i++)
		{
			int j;
			for (j = 0; j < needle.Length && hay[i + j] == needle[j]; j++)
			{
			}
			if (j == needle.Length)
			{
				return i;
			}
		}
		return -1;
	}

	private static long SkipPng(Stream st)
	{
		byte[] array = new byte[8];
		int num;
		for (int i = 0; i < 8; i += num)
		{
			num = st.Read(array, i, 8 - i);
			if (num <= 0)
			{
				return -1L;
			}
		}
		if (array[0] != 137 || array[1] != 80)
		{
			return -1L;
		}
		byte[] array2 = new byte[8];
		string @string;
		do
		{
			int num2;
			for (int i = 0; i < 8; i += num2)
			{
				num2 = st.Read(array2, i, 8 - i);
				if (num2 <= 0)
				{
					return -1L;
				}
			}
			long num3 = (long)(((ulong)array2[0] << 24) | ((ulong)array2[1] << 16) | ((ulong)array2[2] << 8) | array2[3]);
			@string = Encoding.ASCII.GetString(array2, 4, 4);
			long num4 = st.Position + num3 + 4;
			if (num4 > st.Length)
			{
				return -1L;
			}
			st.Position = num4;
		}
		while (!(@string == "IEND"));
		return st.Position;
	}

	private int AllCount()
	{
		lock (_all)
		{
			return _all.Count;
		}
	}

	private void RefreshView()
	{
		if (_multi.Count > 0)
		{
			_multi.Clear();
		}
		_batchMode = false;
		RefreshView(keepPos: false);
	}

	private void RefreshView(bool keepPos)
	{
		List<Entry> list;
		lock (_all)
		{
			list = new List<Entry>(_all);
		}
		int chip = _chip;
		string author = _author;
		string sub = _sub;
		List<Entry> list2 = new List<Entry>();
		for (int i = 0; i < list.Count; i++)
		{
			Entry entry = list[i];
			if (author != null && !string.Equals(entry.Author ?? "", author, StringComparison.OrdinalIgnoreCase))
			{
				continue;
			}
			if (sub != null)
			{
				string text = entry.Sub ?? "";
				if (sub.Length == 0)
				{
					if (text.Length != 0)
					{
						continue;
					}
				}
				else if (!string.Equals(text, sub, StringComparison.OrdinalIgnoreCase) && !text.StartsWith(sub + "/", StringComparison.OrdinalIgnoreCase))
				{
					continue;
				}
			}
			switch (chip)
			{
			case 1:
				if (!_favs.Contains(entry.Path))
				{
					continue;
				}
				break;
			case 2:
				if (_recent.IndexOf(entry.Path) < 0)
				{
					continue;
				}
				break;
			}
			if (_verMode == 1)
			{
				if (entry.Ver == null || !entry.Ver.StartsWith("1.1"))
				{
					continue;
				}
			}
			else if (_verMode == 2 && (entry.Ver == null || !entry.Ver.StartsWith("1.0")))
			{
				continue;
			}
			if (_sizeMode > 0)
			{
				double num = (double)entry.Size / 1048576.0;
				if (_sizeMode switch
				{
					1 => (num < 5.0) ? 1 : 0, 
					2 => (num >= 5.0 && num < 20.0) ? 1 : 0, 
					3 => (num >= 20.0 && num < 50.0) ? 1 : 0, 
					_ => (num >= 50.0) ? 1 : 0, 
				} == 0)
				{
					continue;
				}
			}
			if (_durMode > 0)
			{
				if (entry.DurParsed == 0)
				{
					continue;
				}
				float dur = entry.Dur;
				if (dur <= 0f || _durMode switch
				{
					1 => (dur < 10f) ? 1 : 0, 
					2 => (dur >= 10f && dur < 15f) ? 1 : 0, 
					3 => (dur >= 15f && dur < 30f) ? 1 : 0, 
					4 => (dur >= 30f && dur < 60f) ? 1 : 0, 
					6 => ((_durMin <= 0f || dur >= _durMin - 0.001f) && (_durMax <= 0f || dur <= _durMax + 0.001f)) ? 1 : 0, 
					_ => (dur >= 60f) ? 1 : 0, 
				} == 0)
				{
					continue;
				}
			}
			if (!string.IsNullOrEmpty(_collFilter))
			{
				Coll coll = CollByName(_collFilter);
				if (coll == null || !coll.Paths.Contains(entry.Path))
				{
					continue;
				}
			}
			if (_searchText.Length <= 0 || MatchesSearch(entry))
			{
				list2.Add(entry);
			}
		}
		Comparison<Entry> comparison;
		switch (_sortMode)
		{
		case 1:
			comparison = delegate(Entry a, Entry b)
			{
				int num3 = a.CTime.CompareTo(b.CTime);
				return (num3 == 0) ? a.MTime.CompareTo(b.MTime) : num3;
			};
			break;
		case 2:
			comparison = (Entry a, Entry b) => string.Compare(a.Name, b.Name, StringComparison.CurrentCultureIgnoreCase);
			break;
		case 3:
			comparison = (Entry a, Entry b) => a.Size.CompareTo(b.Size);
			break;
		case 4:
			comparison = delegate(Entry a, Entry b)
			{
				int num2 = string.Compare(a.Ver ?? "", b.Ver ?? "", StringComparison.Ordinal);
				return (num2 == 0) ? a.MTime.CompareTo(b.MTime) : num2;
			};
			break;
		case 5:
		{
			bool asc = !_sortDesc;
			comparison = delegate(Entry a, Entry b)
			{
				bool flag = a.DurParsed == 0 || a.Dur <= 0f;
				bool flag2 = b.DurParsed == 0 || b.Dur <= 0f;
				if (flag || flag2)
				{
					if (flag && flag2)
					{
						return a.MTime.CompareTo(b.MTime);
					}
					if (!flag)
					{
						return -1;
					}
					return 1;
				}
				return (!asc) ? b.Dur.CompareTo(a.Dur) : a.Dur.CompareTo(b.Dur);
			};
			break;
		}
		default:
			comparison = (Entry a, Entry b) => a.MTime.CompareTo(b.MTime);
			break;
		}
		list2.Sort(comparison);
		if (_sortDesc && _sortMode != 5)
		{
			list2.Reverse();
		}
		string text2 = null;
		if (keepPos && _selected >= 0 && _selected < _view.Count)
		{
			text2 = _view[_selected].Path;
		}
		float scrollY = _scrollY;
		_view.Clear();
		_view.AddRange(list2);
		_selected = -1;
		_scrollY = (keepPos ? scrollY : 0f);
		if (text2 != null)
		{
			for (int j = 0; j < _view.Count; j++)
			{
				if (string.Equals(_view[j].Path, text2, StringComparison.OrdinalIgnoreCase))
				{
					_selected = j;
					break;
				}
			}
		}
		ClearCells();
		UpdateToggleVisuals();
		UpdateSortButtonVisuals();
		UpdateSelectionLabel();
		RefreshDetail();
	}

	private void TouchTex(Entry e)
	{
		if (e == null || (Object)(object)e.Tex == (Object)null)
		{
			return;
		}
		_texLru.Remove(e);
		_texLru.Add(e);
		if (_texLru.Count <= 260)
		{
			return;
		}
		int num = Mathf.Max(0, (int)Mathf.Floor(_scrollY / (_cellH + 12f)));
		int num2 = Mathf.CeilToInt(_viewH / (_cellH + 12f)) + 1;
		int num3 = num * _cols;
		int num4 = Mathf.Min(_view.Count - 1, (num + num2) * _cols - 1);
		while (_texLru.Count > 260)
		{
			Entry entry = null;
			for (int i = 0; i < _texLru.Count; i++)
			{
				Entry entry2 = _texLru[i];
				int num5 = _view.IndexOf(entry2);
				if (num5 < num3 || num5 > num4)
				{
					entry = entry2;
					break;
				}
			}
			if (entry != null)
			{
				_texLru.Remove(entry);
				if ((Object)(object)entry.Tex != (Object)null)
				{
					Object.Destroy((Object)(object)entry.Tex);
					entry.Tex = null;
				}
				continue;
			}
			break;
		}
	}

	private void PumpThumbnails()
	{
		if (_view.Count == 0)
		{
			return;
		}
		float num = ((_thumbBudgetMs != null) ? _thumbBudgetMs.Value : 8f);
		float num2 = Time.realtimeSinceStartup * 1000f;
		int num3 = Mathf.Max(0, (int)Mathf.Floor(_scrollY / (_cellH + 12f)));
		int num4 = Mathf.CeilToInt(_viewH / (_cellH + 12f)) + 2;
		int num5 = num3 * _cols;
		int num6 = Mathf.Min(_view.Count - 1, (num3 + num4) * _cols - 1);
		int num7 = 0;
		for (int i = num5; i <= num6; i++)
		{
			Entry entry = _view[i];
			if ((Object)(object)entry.Tex != (Object)null || entry.TexFailed)
			{
				continue;
			}
			try
			{
				entry.Tex = PngAssist.LoadTexture(entry.Path);
				if ((Object)(object)entry.Tex == (Object)null)
				{
					entry.TexFailed = true;
				}
				else
				{
					if (!_thumbLogged)
					{
						_thumbLogged = true;
						Log.LogInfo((object)("thumb ok: " + entry.Path));
					}
					TouchTex(entry);
				}
			}
			catch (Exception)
			{
				entry.TexFailed = true;
			}
			num7++;
			if (num7 >= 4 || Time.realtimeSinceStartup * 1000f - num2 > num)
			{
				break;
			}
		}
	}

	private void ComputeCellSize()
	{
		_cols = Mathf.Clamp(_colCount, 4, 10);
		_cellW = Mathf.Floor((ViewW - 20f - 12f * (float)(_cols - 1)) / (float)_cols);
		_cellH = Mathf.Floor(_cellW * 9f / 16f) + 46f;
		float num = ViewH;
		float num2 = (num - 16f + 12f) / 3f - 12f;
		if (_cellH > num2)
		{
			_cellH = Mathf.Floor(num2);
			_cellW = Mathf.Min(_cellW, Mathf.Floor((_cellH - 46f) * 16f / 9f));
		}
		float num3 = (float)_cols * _cellW + (float)(_cols - 1) * 12f;
		_gridOffX = 10f + Mathf.Floor((ViewW - 20f - num3) / 2f);
	}

	private void UpdateScrollAndCells()
	{
		if ((Object)(object)_contentRect == (Object)null)
		{
			return;
		}
		ComputeCellSize();
		float num = _cellH + 12f;
		float num2 = ViewH;
		int num3 = Mathf.Max(1, Mathf.FloorToInt((num2 - 16f + 12f) / num));
		float num4 = (float)num3 * num - 12f + 16f;
		if (num4 > num2)
		{
			num4 = num2;
		}
		if (Mathf.Abs(num4 - _viewH) > 0.01f)
		{
			_viewH = num4;
			if ((Object)(object)_viewportRect != (Object)null)
			{
				_viewportRect.sizeDelta = new Vector2(ViewW, _viewH);
				_viewportRect.anchoredPosition = new Vector2(ViewX, ViewTop - _viewH / 2f);
			}
			if ((Object)(object)_sbTrack != (Object)null)
			{
				_sbTrack.sizeDelta = new Vector2(6f, _viewH);
				_sbTrack.anchoredPosition = new Vector2(_sbX, ViewTop - _viewH / 2f);
			}
			_contentRect.sizeDelta = new Vector2(ViewW, _viewH);
		}
		int count = _view.Count;
		int num5 = ((count != 0) ? ((count + _cols - 1) / _cols) : 0);
		_contentH = ((num5 == 0) ? 0f : (16f + (float)num5 * _cellH + (float)(num5 - 1) * 12f));
		float num6 = Mathf.Max(0f, _contentH - _viewH);
		if (_scrollY > num6)
		{
			_scrollY = num6;
		}
		if (_scrollY < 0f)
		{
			_scrollY = 0f;
		}
		int num7 = Mathf.Max(0, (int)Mathf.Floor(_scrollY / num) - 1);
		int num8 = Mathf.Min(num5 - 1, num7 + num3 + 1);
		List<int> list = new List<int>();
		if (count > 0 && num8 >= num7)
		{
			for (int i = num7; i <= num8; i++)
			{
				for (int j = 0; j < _cols; j++)
				{
					int num9 = i * _cols + j;
					if (num9 >= count)
					{
						break;
					}
					list.Add(num9);
					if (!_cells.ContainsKey(num9))
					{
						GameObject val = CreateCell(num9);
						if ((Object)(object)val != (Object)null)
						{
							_cells[num9] = val;
						}
					}
					LayoutCell(num9);
				}
			}
		}
		List<int> list2 = new List<int>();
		foreach (KeyValuePair<int, GameObject> cell in _cells)
		{
			if (!list.Contains(cell.Key))
			{
				list2.Add(cell.Key);
			}
		}
		for (int k = 0; k < list2.Count; k++)
		{
			if (_cells.TryGetValue(list2[k], out var value) && (Object)(object)value != (Object)null)
			{
				Object.Destroy((Object)(object)value);
			}
			_cells.Remove(list2[k]);
		}
		_contentRect.anchoredPosition = new Vector2(0f, _scrollY);
		if ((Object)(object)_sbThumb != (Object)null && (Object)(object)_sbTrack != (Object)null)
		{
			float num10 = ((_contentH <= _viewH) ? 1f : (_viewH / _contentH));
			float num11 = Mathf.Max(40f, _viewH * num10);
			_sbThumb.sizeDelta = new Vector2(6f, num11);
			float num12 = 0f;
			if (_contentH > _viewH)
			{
				float num13 = _scrollY / (_contentH - _viewH);
				num12 = (0f - num13) * (_viewH - num11);
			}
			_sbThumb.anchoredPosition = new Vector2(0f, num12);
			bool flag = _contentH > _viewH;
			if (((Component)_sbTrack).gameObject.activeSelf != flag)
			{
				((Component)_sbTrack).gameObject.SetActive(flag);
			}
		}
		UpdatePager(num3, num5, num);
	}

	private void LayoutCell(int idx)
	{
		if (!_cells.TryGetValue(idx, out var value) || (Object)(object)value == (Object)null || idx < 0 || idx >= _view.Count)
		{
			return;
		}
		RectTransform component = value.GetComponent<RectTransform>();
		int num = idx / _cols;
		int num2 = idx % _cols;
		float num3 = _gridOffX + (float)num2 * (_cellW + 12f);
		float num4 = 8f + (float)num * (_cellH + 12f);
		component.sizeDelta = new Vector2(_cellW, _cellH);
		component.anchoredPosition = new Vector2(num3, 0f - num4);
		Entry entry = _view[idx];
		bool flag = idx == _selected;
		bool flag2 = _multi.Count > 0 && _multi.Contains(entry.Path);
		Image component2 = value.GetComponent<Image>();
		if ((Object)(object)component2 != (Object)null)
		{
			((Graphic)component2).color = (Color)((flag2 || flag) ? ColAccentLine : ColCard);
		}
		Transform bgT = value.transform.Find("Bg");
		if ((Object)(object)bgT != (Object)null)
		{
			Image bg = ((Component)bgT).GetComponent<Image>();
			if ((Object)(object)bg != (Object)null)
			{
				((Graphic)bg).color = (flag2 ? MultiCardCol : (flag ? ColCardSel : ColCard));
			}
			RectTransform bgRt = (RectTransform)(object)((bgT is RectTransform) ? bgT : null);
			if ((Object)(object)bgRt != (Object)null)
			{
				bgRt.sizeDelta = new Vector2(_cellW - 2f, _cellH - 2f);
			}
		}
		Transform val = value.transform.Find("Thumb");
		if ((Object)(object)val != (Object)null)
		{
			Transform imgT = ((Component)val).transform.Find("Img");
			RawImage component3 = ((Object)(object)imgT != (Object)null) ? ((Component)imgT).GetComponent<RawImage>() : null;
			if ((Object)(object)component3 != (Object)null)
			{
				if ((Object)(object)component3.texture != (Object)(object)entry.Tex)
				{
					component3.texture = (Texture)(object)entry.Tex;
				}
				((Graphic)component3).color = (Color)(((Object)(object)entry.Tex != (Object)null) ? Color.white : new Color(1f, 1f, 1f, 0f));
			}
			Image maskImg = ((Component)val).GetComponent<Image>();
			if ((Object)(object)maskImg != (Object)null)
			{
				((Graphic)maskImg).color = UiTheme.ThumbBg;
			}
			RectTransform component4 = ((Component)val).GetComponent<RectTransform>();
			if ((Object)(object)component4 != (Object)null)
			{
				component4.sizeDelta = new Vector2(_cellW - 12f, _cellH - 58f);
			}
		}
		Transform val2 = value.transform.Find("Name");
		if ((Object)(object)val2 != (Object)null)
		{
			Text component5 = ((Component)val2).GetComponent<Text>();
			if ((Object)(object)component5 != (Object)null)
			{
				string text = Clip(entry.Name, Mathf.Max(4, Mathf.FloorToInt((_cellW - 32f) / 13f)));
				if (component5.text != "<color=#FFFFFF00><size=1>\u180e</size></color>" + text)
				{
					component5.text = "<color=#FFFFFF00><size=1>\u180e</size></color>" + text;
				}
				((Graphic)component5).color = (flag ? ColText : ColCardText);
				RectTransform component6 = ((Component)val2).GetComponent<RectTransform>();
				if ((Object)(object)component6 != (Object)null)
				{
					component6.sizeDelta = new Vector2(_cellW - 32f, 20f);
				}
			}
		}
		Transform val3 = value.transform.Find("Sub");
		if ((Object)(object)val3 != (Object)null)
		{
			Text component7 = ((Component)val3).GetComponent<Text>();
			if ((Object)(object)component7 != (Object)null)
			{
				string text2 = FormatSize(entry.Size);
				if (entry.ObjCount > 0)
				{
					text2 = text2 + "  ·  " + entry.ObjCount + " 对象";
				}
				int num5 = Mathf.Max(4, Mathf.FloorToInt((_cellW - 32f) / 12f));
				int num6 = 0;
				for (int i = 0; i < text2.Length; i++)
				{
					num6 += ((text2[i] <= '⺀') ? 1 : 2);
				}
				int budget = Mathf.Max(2, num5 - num6 - 4);
				string text3 = Clip(entry.Dir ?? "", budget);
				string text4 = ((text3.Length > 0) ? (text3 + "  ·  " + text2) : text2);
				if (component7.text != "<color=#FFFFFF00><size=1>\u180e</size></color>" + text4)
				{
					component7.text = "<color=#FFFFFF00><size=1>\u180e</size></color>" + text4;
				}
				RectTransform component8 = ((Component)val3).GetComponent<RectTransform>();
				if ((Object)(object)component8 != (Object)null)
				{
					component8.sizeDelta = new Vector2(_cellW - 32f, 16f);
				}
			}
		}
		Transform val4 = value.transform.Find("FavStar");
		if ((Object)(object)val4 != (Object)null)
		{
			bool flag3 = _favs.Contains(entry.Path);
			if (((Component)val4).gameObject.activeSelf != flag3)
			{
				((Component)val4).gameObject.SetActive(flag3);
			}
		}
		Transform val5 = value.transform.Find("SelOverlay");
		if ((Object)(object)val5 != (Object)null)
		{
			if (((Component)val5).gameObject.activeSelf != flag2)
			{
				((Component)val5).gameObject.SetActive(flag2);
			}
			RectTransform val6 = (RectTransform)(object)((val5 is RectTransform) ? val5 : null);
			if ((Object)(object)val6 != (Object)null)
			{
				Vector2 val7 = default(Vector2);
				val7 = new Vector2(_cellW - 12f, _cellH - 58f);
				if (val6.sizeDelta != val7)
				{
					val6.sizeDelta = val7;
				}
			}
		}
		Transform val8 = value.transform.Find("MultiMark");
		if ((Object)(object)val8 != (Object)null && ((Component)val8).gameObject.activeSelf != flag2)
		{
			((Component)val8).gameObject.SetActive(flag2);
		}
	}

	private void RefreshDetail()
	{
		if ((Object)(object)_detailGo == (Object)null)
		{
			return;
		}
		bool flag = _selected >= 0 && _selected < _view.Count;
		if (_detailGo.activeSelf != flag)
		{
			_detailGo.SetActive(flag);
		}
		if ((Object)(object)_detailEmpty != (Object)null && _detailEmpty.gameObject.activeSelf == flag)
		{
			_detailEmpty.gameObject.SetActive(!flag);
		}
		if (!flag)
		{
			_detailTexPath = null;
			return;
		}
		Entry entry = _view[_selected];
		if ((Object)(object)_detailName != (Object)null)
		{
			string text = "<color=#FFFFFF00><size=1>\u180e</size></color>" + Clip(entry.Name, 26);
			if (_detailName.text != text)
			{
				_detailName.text = text;
			}
		}
		if ((Object)(object)_detailFile != (Object)null)
		{
			string text2 = "<color=#FFFFFF00><size=1>\u180e</size></color>" + Clip(entry.Name + ".png", 40);
			if (_detailFile.text != text2)
			{
				_detailFile.text = text2;
			}
		}
		if ((Object)(object)_detailBadge != (Object)null)
		{
			bool flag2 = !string.IsNullOrEmpty(entry.Ver);
			if (((Component)_detailBadge).gameObject.activeSelf != flag2)
			{
				((Component)_detailBadge).gameObject.SetActive(flag2);
			}
			if (flag2)
			{
				bool flag3 = entry.Ver.StartsWith("1.1");
				Image component = ((Component)_detailBadge).GetComponent<Image>();
				if ((Object)(object)component != (Object)null)
				{
					((Graphic)component).color = (flag3 ? ColKks : ColKk);
				}
				if ((Object)(object)_detailBadgeText != (Object)null)
				{
					_detailBadgeText.text = ((!string.IsNullOrEmpty(entry.Ver)) ? entry.Ver : (flag3 ? "KKS" : "KK"));
				}
				if ((Object)(object)_detailNote != (Object)null)
				{
					string text3 = "<color=#FFFFFF00><size=1>\u180e</size></color>" + (flag3 ? "KKS 场景卡 · 本作无法读取" : "KK 场景卡");
					if (_detailNote.text != text3)
					{
						_detailNote.text = text3;
					}
				}
			}
		}
		string text4 = entry.Author ?? "";
		string text5 = ((entry.DurParsed == 0) ? "解析中…" : ((entry.Dur > 0f) ? FormatDur(entry.Dur) : "—"));
		string[] array = new string[8]
		{
			(text4.Length == 0) ? "未分类" : text4,
			(entry.ObjCount > 0) ? (entry.ObjCount + " 个") : ((entry.VerParsed == 0) ? "解析中…" : "未知"),
			FormatSize(entry.Size),
			text5,
			(entry.CTime == DateTime.MinValue) ? "未知" : entry.CTime.ToString("yyyy-MM-dd HH:mm"),
			(entry.MTime == DateTime.MinValue) ? "未知" : entry.MTime.ToString("yyyy-MM-dd HH:mm"),
			string.IsNullOrEmpty(entry.Ver) ? "未知" : entry.Ver,
			string.IsNullOrEmpty(entry.Ver) ? "未知" : (entry.Ver.StartsWith("1.1") ? "KKS 场景卡" : "KK 场景卡")
		};
		for (int i = 0; i < _detailVals.Count && i < array.Length; i++)
		{
			string text6 = "<color=#FFFFFF00><size=1>\u180e</size></color>" + Clip(array[i], 22);
			if (_detailVals[i].text != text6)
			{
				_detailVals[i].text = text6;
			}
		}
		if ((Object)(object)_detailPath != (Object)null)
		{
			string text7 = "<color=#FFFFFF00><size=1>\u180e</size></color>" + Clip(entry.Path ?? "", 120);
			if (_detailPath.text != text7)
			{
				_detailPath.text = text7;
			}
		}
		if ((Object)(object)_detailSameDirText != (Object)null)
		{
			string text8 = "<color=#FFFFFF00><size=1>\u180e</size></color>同目录 " + DirCount(Path.GetDirectoryName(entry.Path)) + " 张";
			if (_detailSameDirText.text != text8)
			{
				_detailSameDirText.text = text8;
			}
		}
		if ((Object)(object)_detailThumb != (Object)null && _detailTexPath != entry.Path)
		{
			_detailTexPath = entry.Path;
			Texture2D val = entry.Tex;
			if ((Object)(object)val == (Object)null && !entry.TexFailed)
			{
				try
				{
					val = PngAssist.LoadTexture(entry.Path);
					if ((Object)(object)val == (Object)null)
					{
						entry.TexFailed = true;
					}
				}
				catch (Exception)
				{
					entry.TexFailed = true;
				}
			}
			_detailThumb.texture = (Texture)(object)val;
			((Graphic)_detailThumb).color = (Color)(((Object)(object)val != (Object)null) ? Color.white : new Color(1f, 1f, 1f, 0f));
		}
		if ((Object)(object)_detailFavText != (Object)null)
		{
			bool flag4 = _favs.Contains(entry.Path);
			_detailFavText.text = "<color=#FFFFFF00><size=1>\u180e</size></color>" + (flag4 ? "★ 已收藏" : "★ 收藏");
			((Graphic)_detailFavText).color = (Color)(flag4 ? ColGold : ColSub);
		}
	}

	private static string Clip(string s, int budget)
	{
		if (string.IsNullOrEmpty(s))
		{
			return "";
		}
		int num = 0;
		for (int i = 0; i < s.Length; i++)
		{
			int num2 = ((s[i] <= '⺀') ? 1 : 2);
			if (num + num2 > budget - 1)
			{
				return s.Substring(0, i).TrimEnd() + "…";
			}
			num += num2;
		}
		return s;
	}

	private static string FormatSize(long b)
	{
		if (b >= 1073741824)
		{
			return ((double)b / 1073741824.0).ToString("0.00", CultureInfo.InvariantCulture) + " GB";
		}
		if (b >= 1048576)
		{
			return ((double)b / 1048576.0).ToString("0.0", CultureInfo.InvariantCulture) + " MB";
		}
		if (b >= 1024)
		{
			return ((double)b / 1024.0).ToString("0", CultureInfo.InvariantCulture) + " KB";
		}
		return b + " B";
	}

	private void ClearCells()
	{
		foreach (KeyValuePair<int, GameObject> cell in _cells)
		{
			if ((Object)(object)cell.Value != (Object)null)
			{
				Object.Destroy((Object)(object)cell.Value);
			}
		}
		_cells.Clear();
	}

	private static Texture2D PlaceholderTex()
	{
		if ((Object)(object)_phTex != (Object)null)
		{
			return _phTex;
		}
		_phTex = new Texture2D(64, 64, (TextureFormat)4, false);
		Color val = default(Color);
		val = UiTheme.ThumbBg;
		Color val2 = default(Color);
		val2 = new Color(0.10f, 0.14f, 0.18f, 1f);
		Color[] array = (Color[])(object)new Color[4096];
		for (int i = 0; i < 64; i++)
		{
			for (int j = 0; j < 64; j++)
			{
				int num = Mathf.Abs(j - i);
				int num2 = Mathf.Abs(j + i - 63);
				array[i * 64 + j] = ((num <= 1 || num2 <= 1) ? val2 : val);
			}
		}
		_phTex.SetPixels(array);
		_phTex.Apply();
		((Texture)_phTex).wrapMode = (TextureWrapMode)1;
		return _phTex;
	}

	private Font UiFont()
	{
		if ((Object)(object)_font != (Object)null)
		{
			return _font;
		}
		if (_fontTried)
		{
			return _font;
		}
		_fontTried = true;
		try
		{
			_font = Font.CreateDynamicFontFromOSFont(new string[6] { "Microsoft YaHei UI", "Microsoft YaHei", "SimHei", "Noto Sans CJK SC", "Segoe UI", "Arial" }, 16);
		}
		catch (Exception)
		{
			_font = null;
		}
		if ((Object)(object)_font != (Object)null)
		{
			Log.LogInfo((object)"font: system dynamic");
			return _font;
		}
		string[] array = new string[2] { "LegacyRuntime.ttf", "Arial.ttf" };
		for (int i = 0; i < array.Length; i++)
		{
			try
			{
				_font = Resources.GetBuiltinResource<Font>(array[i]);
			}
			catch (Exception)
			{
				_font = null;
			}
			if ((Object)(object)_font != (Object)null)
			{
				Log.LogInfo((object)("font: builtin " + array[i]));
				return _font;
			}
		}
		try
		{
			Text[] array2 = Resources.FindObjectsOfTypeAll<Text>();
			for (int j = 0; j < array2.Length && j < 4000; j++)
			{
				if ((Object)(object)array2[j] != (Object)null && (Object)(object)array2[j].font != (Object)null)
				{
					_font = array2[j].font;
					break;
				}
			}
		}
		catch (Exception)
		{
		}
		if ((Object)(object)_font != (Object)null)
		{
			Log.LogInfo((object)"font: borrowed from scene");
			return _font;
		}
		Log.LogError((object)"font: NONE available");
		return _font;
	}

	private void EnsureUi()
	{
		if ((Object)(object)_canvasGo == (Object)null)
		{
			_canvasGo = new GameObject("KK_SceneBrowser_Canvas");
			Object.DontDestroyOnLoad((Object)(object)_canvasGo);
			Canvas val = _canvasGo.AddComponent<Canvas>();
			val.renderMode = (RenderMode)0;
			val.sortingOrder = 200;
			_canvas = val;
			CanvasScaler val2 = _canvasGo.AddComponent<CanvasScaler>();
			val2.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
			val2.referenceResolution = new Vector2(1920f, 1080f);
			val2.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
			val2.matchWidthOrHeight = 0.5f;
			_canvasGo.AddComponent<GraphicRaycaster>();
			_canvasRect = _canvasGo.GetComponent<RectTransform>();
		}
		if (!_uiBuilt)
		{
			BuildUi();
		}
	}

	private RectTransform NewRect(string name, Transform parent, float w, float h, float x, float y, Color color, bool raycast)
	{
		GameObject val = new GameObject(name);
		RectTransform val2 = val.AddComponent<RectTransform>();
		((Transform)val2).SetParent(parent, false);
		val2.anchorMin = new Vector2(0.5f, 0.5f);
		val2.anchorMax = new Vector2(0.5f, 0.5f);
		val2.pivot = new Vector2(0.5f, 0.5f);
		val2.sizeDelta = new Vector2(w, h);
		val2.anchoredPosition = new Vector2(x, y);
		if (color.a > 0f || raycast)
		{
			Image val3 = val.AddComponent<Image>();
			((Graphic)val3).color = color;
			((Graphic)val3).raycastTarget = raycast;
		}
		return val2;
	}

	private Text NewLabel(Transform parent, string txt, float w, float h, float x, float y, int size, Color color, TextAnchor anchor)
	{
		GameObject val = new GameObject("Label");
		RectTransform val2 = val.AddComponent<RectTransform>();
		((Transform)val2).SetParent(parent, false);
		val2.anchorMin = new Vector2(0.5f, 0.5f);
		val2.anchorMax = new Vector2(0.5f, 0.5f);
		val2.pivot = new Vector2(0.5f, 0.5f);
		val2.sizeDelta = new Vector2(w, h);
		val2.anchoredPosition = new Vector2(x, y);
		Text val3 = val.AddComponent<Text>();
		val3.font = UiFont();
		val3.fontSize = size;
		((Graphic)val3).color = color;
		val3.alignment = anchor;
		val3.horizontalOverflow = (HorizontalWrapMode)1;
		val3.verticalOverflow = (VerticalWrapMode)1;
		((Graphic)val3).raycastTarget = false;
		val3.supportRichText = true;
		val3.text = "<color=#FFFFFF00><size=1>\u180e</size></color>" + txt;
		return val3;
	}

	private Button NewButton(Transform parent, string txt, float w, float h, float x, float y, UnityAction onClick)
	{
		RectTransform val = NewRect("Btn_" + txt, parent, w, h, x, y, ColField, raycast: true);
		Image img = ((Component)val).GetComponent<Image>();
		UiTheme.Round(img, RChip);
		Button val2 = ((Component)val).gameObject.AddComponent<Button>();
		((Selectable)val2).targetGraphic = (Graphic)(object)img;
		UiTheme.SetButtonFeedback(val2);
		NewLabel((Transform)(object)val, txt, w, h, 0f, 0f, 14, ColSub, (TextAnchor)4);
		if (onClick != null)
		{
			((UnityEvent)val2.onClick).AddListener(onClick);
		}
		return val2;
	}

	/// <summary>扁平卡片：外层 1px 描边 + 内层圆角填充，返回内层（子节点挂这里）。</summary>
	private RectTransform NewCard(string name, Transform parent, float w, float h, float x, float y, Color fill, int radius, bool raycast)
	{
		RectTransform outer = NewRect(name, parent, w, h, x, y, ColBorder, raycast);
		UiTheme.Round(((Component)outer).GetComponent<Image>(), radius);
		RectTransform inner = NewRect("Fill", (Transform)(object)outer, w - 2f, h - 2f, 0f, 0f, fill, raycast: false);
		UiTheme.Round(((Component)inner).GetComponent<Image>(), Mathf.Max(2, radius - 1));
		((Transform)inner).SetSiblingIndex(0);
		return inner;
	}

	/// <summary>圆角纯色块（背景板 / 分隔 / 选中态等）。</summary>
	private RectTransform NewRound(string name, Transform parent, float w, float h, float x, float y, Color color, int radius, bool raycast)
	{
		RectTransform val = NewRect(name, parent, w, h, x, y, color, raycast);
		UiTheme.Round(((Component)val).GetComponent<Image>(), radius);
		return val;
	}

	private static Text ButtonLabel(Button btn)
	{
		if ((Object)(object)btn == (Object)null)
		{
			return null;
		}
		Transform val = ((Component)btn).transform.Find("Label");
		if ((Object)(object)val == (Object)null)
		{
			return null;
		}
		return ((Component)val).GetComponent<Text>();
	}

	private static void PaintButton(Button btn, Color bg, Color fg)
	{
		if ((Object)(object)btn == (Object)null)
		{
			return;
		}
		Image img = ((Component)btn).GetComponent<Image>();
		if ((Object)(object)img != (Object)null)
		{
			((Graphic)img).color = bg;
		}
		Text lbl = ButtonLabel(btn);
		if ((Object)(object)lbl != (Object)null)
		{
			((Graphic)lbl).color = fg;
		}
	}

	/// <summary>把 RawImage 包进圆角遮罩容器里（返回 RawImage）。</summary>
	private RawImage NewRoundImage(string name, Transform parent, float w, float h, float x, float y, int radius, Color bg)
	{
		RectTransform host = NewRect(name, parent, w, h, x, y, bg, raycast: false);
		Image hostImg = ((Component)host).GetComponent<Image>();
		if ((Object)(object)hostImg == (Object)null)
		{
			hostImg = ((Component)host).gameObject.AddComponent<Image>();
		}
		((Graphic)hostImg).color = bg;
		((Graphic)hostImg).raycastTarget = false;
		UiTheme.Round(hostImg, radius);
		Mask mask = ((Component)host).gameObject.AddComponent<Mask>();
		mask.showMaskGraphic = true;
		GameObject go = new GameObject("Img");
		RectTransform rt = go.AddComponent<RectTransform>();
		((Transform)rt).SetParent((Transform)(object)host, false);
		rt.anchorMin = Vector2.zero;
		rt.anchorMax = Vector2.one;
		rt.offsetMin = Vector2.zero;
		rt.offsetMax = Vector2.zero;
		RawImage raw = go.AddComponent<RawImage>();
		((Graphic)raw).raycastTarget = false;
		raw.uvRect = new Rect(0f, 0f, 1f, 1f);
		((Graphic)raw).color = Color.white;
		return raw;
	}

	private void BuildUi()
	{
		_uiBuilt = true;
		GameObject val = new GameObject("Dim");
		_rootRect = val.AddComponent<RectTransform>();
		((Transform)_rootRect).SetParent((Transform)(object)_canvasRect, false);
		_rootRect.anchorMin = Vector2.zero;
		_rootRect.anchorMax = Vector2.one;
		_rootRect.pivot = new Vector2(0.5f, 0.5f);
		_rootRect.offsetMin = Vector2.zero;
		_rootRect.offsetMax = Vector2.zero;
		Image val2 = val.AddComponent<Image>();
		((Graphic)val2).color = ColBg;
		((Graphic)val2).raycastTarget = true;
		_rootGo = val;
		NewRound("Shadow", (Transform)(object)_rootRect, PanelW + 22f, 996f, 0f, -8f, new Color(0f, 0f, 0f, 0.45f), RPanel + 6, false);
		RectTransform val3 = NewCard("Panel", (Transform)(object)_rootRect, PanelW, 980f, 0f, 0f, ColPanel, RPanel, true);
		float num = 490f;
		Text val4 = NewLabel((Transform)(object)val3, "SD卡管理器", 300f, 32f, PanelLeft + 172f, num - 26f, 21, ColText, (TextAnchor)3);
		((Object)val4).name = "Title";
		_pathLabel = NewLabel((Transform)(object)val3, "", PanelW - 242f, 18f, PanelLeft + 22f + (PanelW - 242f) / 2f, num - 52f, 12, ColDim, (TextAnchor)3);
		((Object)_pathLabel).name = "PathLabel";
		_statusLabel = NewLabel((Transform)(object)val3, "", 300f, 18f, PanelRight - 66f - 150f, num - 52f, 12, ColDim, (TextAnchor)5);
		((Object)_statusLabel).name = "StatusLabel";
		Button val5 = NewButton((Transform)(object)val3, "×", 34f, 34f, PanelRight - 34f, num - 24f, new UnityAction(OnClickClose));
		((Object)val5).name = "Btn_Close";
		Text closeLbl = ButtonLabel(val5);
		if ((Object)(object)closeLbl != (Object)null)
		{
			closeLbl.fontSize = 20;
		}
		PaintButton(val5, ColField, ColSub);
		Button refreshBtn = NewButton((Transform)(object)val3, "更新索引", 92f, 26f, 648f, num - 24f, new UnityAction(OnClickRefreshIndex));
		((Object)refreshBtn).name = "Btn_RefreshIndex";
		Text refreshLbl = ButtonLabel(refreshBtn);
		if ((Object)(object)refreshLbl != (Object)null)
		{
			refreshLbl.fontSize = 11;
		}
		NewRect("Sep1", (Transform)(object)val3, PanelW - 56f, 1f, 0f, 422f, ColLine, raycast: false);
		NewCard("FilterBar", (Transform)(object)val3, GridRight - GridLeft, 96f, (GridLeft + GridRight) / 2f, 370f, ColSurface, RCard, false);
		float num2 = num - 146f;
		float y = (_chipY = num - 96f);
		_countLabel = NewLabel((Transform)(object)val3, "", 96f, 20f, 679f, num2, 12, ColSub, (TextAnchor)5);
		((Object)_countLabel).name = "CountLabel";
		_sliderW = 200f;
		float trackLeft = 251f;
		_sliderTrack = NewRound("SizeTrack", (Transform)(object)val3, _sliderW, 6f, trackLeft + _sliderW / 2f, num2, ColLine, 2, true);
		_sliderFill = NewRound("SizeFill", (Transform)(object)_sliderTrack, SliderFillW(_colCount), 6f, 0f, 0f, ColAccent, 2, false);
		_sliderFill.anchorMin = new Vector2(0f, 0.5f);
		_sliderFill.anchorMax = new Vector2(0f, 0.5f);
		_sliderFill.pivot = new Vector2(0f, 0.5f);
		_sliderFill.anchoredPosition = Vector2.zero;
		_sliderKnob = NewRound("SizeKnob", (Transform)(object)val3, 14f, 14f, KnobX(trackLeft, _colCount), num2, new Color(0.85f, 0.90f, 0.95f, 1f), 6, false);
		SliderDrag sliderDrag = ((Component)_sliderTrack).gameObject.AddComponent<SliderDrag>();
		sliderDrag.OnValue = OnThumbSlider;
		sliderDrag.Track = _sliderTrack;
		sliderDrag.Width = _sliderW;
		sliderDrag.Radius = 8f;
		NewLabel((Transform)(object)val3, "缩略图", 44f, 20f, 223f, num2, 11, ColDim, (TextAnchor)4);
		Button val6 = NewButton((Transform)(object)val3, "↓", 38f, 32f, 176f, num2, new UnityAction(OnToggleOrder));
		((Object)val6).name = "Btn_Order";
		_orderBtnText = ((Component)((Component)val6).transform.Find("Label")).GetComponent<Text>();
		_sortBtn = NewButton((Transform)(object)val3, "创建日期 ↓", 112f, 32f, 95f, num2, new UnityAction(OnSortClicked));
		((Object)_sortBtn).name = "Btn_Sort";
		_sortBtnText = ((Component)((Component)_sortBtn).transform.Find("Label")).GetComponent<Text>();
		_sortCx = 95f;
		_sortY = num2;
		float num4 = GridLeft + 14f;
		float[] array = new float[7] { 99f, 138f, 99f, 210f, 210f, 210f, 210f };
		int[] order = new int[7] { 0, 6, 1, 2, 3, 4, 5 };
		_chipBtns.Clear();
		_chipTexts.Clear();
		_chipX.Clear();
		for (int k = 0; k < array.Length; k++)
		{
			_chipBtns.Add(null);
			_chipTexts.Add(null);
			_chipX.Add(0f);
		}
		for (int i = 0; i < order.Length; i++)
		{
			int ci = order[i];
			float num5 = array[ci];
			float num6 = num4 + num5 / 2f;
			Button val7 = NewButton((Transform)(object)val3, ChipLabel(ci), num5, 32f, num6, y, (UnityAction)delegate
			{
				OnChipClicked(ci);
			});
			((Object)val7).name = "Chip" + ci;
			_chipBtns[ci] = val7;
			_chipTexts[ci] = ((Component)((Component)val7).transform.Find("Label")).GetComponent<Text>();
			_chipX[ci] = num6;
			num4 += num5 + 8f;
		}
		_viewH = ViewH;
		float y2 = -21f;
		NewCard("GridBg", (Transform)(object)val3, GridRight - GridLeft, 686f, (GridLeft + GridRight) / 2f, y2, UiTheme.Sunken, RCard, false);
		_viewportRect = NewRect("Viewport", (Transform)(object)val3, ViewW, _viewH, ViewX, y2, new Color(0f, 0f, 0f, 0f), raycast: true);
		((Component)_viewportRect).gameObject.AddComponent<RectMask2D>();
		ScrollCatcher scrollCatcher = ((Component)_viewportRect).gameObject.AddComponent<ScrollCatcher>();
		scrollCatcher.WheelHandler = OnWheel;
		scrollCatcher.DragHandler = OnDrag;
		_contentRect = NewRect("Content", (Transform)(object)_viewportRect, 1076f, _viewH, 0f, 0f, new Color(0f, 0f, 0f, 0f), raycast: false);
		_contentRect.anchorMin = new Vector2(0.5f, 1f);
		_contentRect.anchorMax = new Vector2(0.5f, 1f);
		_contentRect.pivot = new Vector2(0.5f, 1f);
		_contentRect.anchoredPosition = new Vector2(0f, 0f);
		_sbX = ScrollX;
		_sbTrack = NewRound("ScrollTrack", (Transform)(object)val3, 6f, _viewH, _sbX, y2, ColLine, 1, true);
		_sbThumb = NewRound("ScrollThumb", (Transform)(object)_sbTrack, 6f, 80f, 0f, 0f, new Color(0.40f, 0.50f, 0.60f, 1f), 1, false);
		_sbThumb.anchorMin = new Vector2(0.5f, 1f);
		_sbThumb.anchorMax = new Vector2(0.5f, 1f);
		_sbThumb.pivot = new Vector2(0.5f, 1f);
		_sbThumb.anchoredPosition = new Vector2(0f, 0f);
		ScrollTrackDrag scrollTrackDrag = ((Component)_sbTrack).gameObject.AddComponent<ScrollTrackDrag>();
		scrollTrackDrag.Track = _sbTrack;
		scrollTrackDrag.OnScrollTo = OnScrollBar;
		BuildSidePanel((Transform)(object)val3);
		NewCard("InfoBar", (Transform)(object)val3, GridRight - GridLeft, 64f, (GridLeft + GridRight) / 2f, -432f, ColSurface, RCard, false);
		_selLabel = NewLabel((Transform)(object)val3, "未选择", 1200f, 22f, (GridLeft + GridRight) / 2f, -432f, 13, ColSub, (TextAnchor)3);
		((Object)_selLabel).name = "SelLabel";
		_batchGo.Clear();
		float x = -501f;
		AddBatchButton((Transform)(object)val3, "全选", 62f, ref x, num2, new UnityAction(SelectAllInView));
		AddBatchButton((Transform)(object)val3, "本页全选", 84f, ref x, num2, new UnityAction(SelectAllVisible));
		AddBatchButton((Transform)(object)val3, "取消", 62f, ref x, num2, new UnityAction(OnClearMulti));
		RectTransform val10 = NewRect("Pager", (Transform)(object)val3, 36f, _viewH, PagerX, y2, new Color(0f, 0f, 0f, 0f), raycast: false);
		_pageGo = ((Component)val10).gameObject;
		_pagePrev = NewButton((Transform)(object)val10, "∧\n∧", 26f, 26f, 0f, 0f, new UnityAction(OnPageFirst));
		((Object)_pagePrev).name = "Btn_PageFirst";
		Text prevLbl = ButtonLabel(_pagePrev);
		if ((Object)(object)prevLbl != (Object)null)
		{
			prevLbl.fontSize = 11;
			prevLbl.lineSpacing = 0.7f;
		}
		_pageBtns.Clear();
		_pageTexts.Clear();
		for (int j = 0; j < 20; j++)
		{
			int slot = j;
			Button val11 = NewButton((Transform)(object)val10, "", 26f, 26f, 0f, 0f, (UnityAction)delegate
			{
				OnPageSlot(slot);
			});
			((Object)val11).name = "Btn_Page" + j;
			Text component4 = ((Component)((Component)val11).transform.Find("Label")).GetComponent<Text>();
			if ((Object)(object)component4 != (Object)null)
			{
				component4.fontSize = 12;
			}
			_pageBtns.Add(val11);
			_pageTexts.Add(component4);
		}
		_pageNext = NewButton((Transform)(object)val10, "∨\n∨", 26f, 26f, 0f, 0f, new UnityAction(OnPageLast));
		((Object)_pageNext).name = "Btn_PageLast";
		Text nextLbl = ButtonLabel(_pageNext);
		if ((Object)(object)nextLbl != (Object)null)
		{
			nextLbl.fontSize = 11;
			nextLbl.lineSpacing = 0.7f;
		}
		_pageGo.SetActive(false);
		BuildSearchBox((Transform)(object)val3);
		UpdateToggleVisuals();
		UpdateSortButtonVisuals();
		_rootGo.SetActive(false);
	}

	private void BuildSearchBox(Transform panel)
	{
		float num = 108f;
		float num2 = 32f;
		float x = 569f;
		float y = 344f;
		RectTransform val = (_searchBox = NewRound("SearchBox", panel, num, num2, x, y, ColBorder, RItem, true));
		_searchGo = ((Component)val).gameObject;
		RectTransform val2 = NewRound("SearchFill", (Transform)(object)val, num - 2f, num2 - 2f, 0f, 0f, UiTheme.Field, RItem - 1, false);
		((Transform)val2).SetAsFirstSibling();
		_searchModeBtn = NewButton((Transform)(object)val, "卡名", 52f, 24f, (0f - num) / 2f - 34f, 0f, new UnityAction(OnToggleSearchMode));
		((Object)_searchModeBtn).name = "Btn_SearchMode";
		Text modeLbl = ButtonLabel(_searchModeBtn);
		if ((Object)(object)modeLbl != (Object)null)
		{
			modeLbl.fontSize = 11;
		}
		float num3 = num - 24f;
		RectTransform val3 = NewRect("FieldArea", (Transform)(object)val, num - 2f, num2 - 2f, 0f, 0f, new Color(0f, 0f, 0f, 0f), raycast: true);
		((Component)val3).gameObject.AddComponent<RectMask2D>();
		Text val4 = NewLabel((Transform)(object)val3, "", num3, 22f, 0f, 0f, 12, ColText, (TextAnchor)3);
		((Object)val4).name = "FieldText";
		val4.supportRichText = true;
		val4.horizontalOverflow = (HorizontalWrapMode)1;
		val4.verticalOverflow = (VerticalWrapMode)0;
		_searchPlaceholder = NewLabel((Transform)(object)val3, "卡名", num3, 22f, 0f, 0f, 12, ColDim, (TextAnchor)3);
		((Object)_searchPlaceholder).name = "Placeholder";
		_searchField = ((Component)val3).gameObject.AddComponent<InputField>();
		_searchField.textComponent = val4;
		_searchField.placeholder = (Graphic)(object)_searchPlaceholder;
		((Selectable)_searchField).targetGraphic = (Graphic)(object)((Component)val3).GetComponent<Image>();
		_searchField.lineType = InputField.LineType.SingleLine;
		_searchField.characterLimit = 64;
		_searchField.text = _searchText;
		((UnityEvent<string>)(object)_searchField.onValueChanged).AddListener((UnityAction<string>)OnSearchChanged);
		((UnityEvent<string>)(object)_searchField.onEndEdit).AddListener((UnityAction<string>)OnSearchEndEdit);
		UpdateSearchModeUi();
	}

	private void OnToggleSearchMode()
	{
		_searchByAuthor = !_searchByAuthor;
		UpdateSearchModeUi();
		RefreshView(keepPos: true);
		FocusSearchField();
	}

	private void UpdateSearchModeUi()
	{
		if ((Object)(object)_searchModeBtn != (Object)null)
		{
			Text text = ButtonLabel(_searchModeBtn);
			if ((Object)(object)text != (Object)null)
			{
				text.text = (_searchByAuthor ? "作者" : "卡名");
			}
			PaintButton(_searchModeBtn, _searchByAuthor ? ColAccent : ColField, _searchByAuthor ? Color.white : ColSub);
		}
		if ((Object)(object)_searchPlaceholder != (Object)null)
		{
			_searchPlaceholder.text = (_searchByAuthor ? "作者名" : "卡名");
		}
	}

	private void OnSearchChanged(string v)
	{
		string text = v ?? "";
		if (!(text == _searchText))
		{
			_searchText = text;
			RefreshView(keepPos: true);
		}
	}

	private void OnSearchEndEdit(string v)
	{
		if ((Object)(object)_searchField != (Object)null)
		{
			_ = _searchField.isFocused;
		}
	}

	private static float SliderT(int cols)
	{
		return Mathf.Clamp01(((float)cols - 4f) / 6f);
	}

	private static float KnobX(float trackLeft, int cols)
	{
		float num = 8f;
		return trackLeft + num + 116f * SliderT(cols);
	}

	private static float SliderFillW(int cols)
	{
		float num = 8f;
		return num + 116f * SliderT(cols);
	}

	private void BuildSidePanel(Transform panel)
	{
		RectTransform parent = NewCard("Side", panel, SideW, SideH, SideCx, -25f, ColSideBg, RCard, true);
		_sideHead = NewLabel((Transform)(object)parent, "作者", 184f, 20f, 0f, 409f, 12, ColDim, (TextAnchor)3);
		((Object)_sideHead).name = "SideHead";
		NewRect("SideSep", (Transform)(object)parent, 184f, 1f, 0f, 395f, ColLine, raycast: false);
		_sideViewH = 805f;
		_sideViewport = NewRect("SideView", (Transform)(object)parent, 192f, _sideViewH, 0f, -19.5f, new Color(0f, 0f, 0f, 0f), raycast: true);
		((Component)_sideViewport).gameObject.AddComponent<RectMask2D>();
		ScrollCatcher scrollCatcher = ((Component)_sideViewport).gameObject.AddComponent<ScrollCatcher>();
		scrollCatcher.WheelHandler = OnSideWheel;
		_sideContent = NewRect("SideContent", (Transform)(object)_sideViewport, 196f, 10f, 0f, 0f, new Color(0f, 0f, 0f, 0f), raycast: false);
		_sideContent.anchorMin = new Vector2(0.5f, 1f);
		_sideContent.anchorMax = new Vector2(0.5f, 1f);
		_sideContent.pivot = new Vector2(0.5f, 1f);
		_sideContent.anchoredPosition = Vector2.zero;
		_sideDirty = true;
	}

	private void BuildDetailPanel(Transform panel)
	{
		float num = ViewH;
		float y = -21f;
		RectTransform card = NewCard("Detail", panel, 300f, num, 662f, y, ColDetailBg, RCard, true);
		RectTransform val = NewRect("DBody", (Transform)(object)card, 294f, num - 2f, 0f, 0f, new Color(0f, 0f, 0f, 0f), raycast: false);
		_detailGo = ((Component)val).gameObject;
		_detailEmpty = NewLabel((Transform)(object)card, "未选择卡片", 200f, 26f, 0f, 0f, 13, ColDim, (TextAnchor)4);
		float num2 = 14f;
		float num3 = 296f - num2 * 2f;
		float num4 = Mathf.Floor(num3 * 9f / 16f);
		float num5 = num / 2f - num2;
		_detailThumb = NewRoundImage("DThumb", (Transform)(object)val, num3, num4, 0f, num5 - num4 / 2f, RItem, UiTheme.ThumbBg);
		num5 -= num4 + 16f;
		Text nameLbl = NewLabel((Transform)(object)val, "", num3, 26f, 0f, num5 - 13f, 19, ColText, (TextAnchor)3);
		_detailName = nameLbl;
		((Object)_detailName).name = "DName";
		num5 -= 30f;
		_detailFile = NewLabel((Transform)(object)val, "", num3, 16f, 0f, num5 - 8f, 11, ColDim, (TextAnchor)3);
		((Object)_detailFile).name = "DFile";
		num5 -= 26f;
		Text val3 = NewLabel((Transform)(object)(_detailBadge = NewRound("DBadge", (Transform)(object)val, 46f, 20f, (0f - num3) / 2f + 23f, num5 - 10f, ColKk, 5, false)), "KK", 46f, 20f, 0f, 0f, 11, Color.white, (TextAnchor)4);
		((Object)val3).name = "DBadgeText";
		_detailBadgeText = val3;
		_detailNote = NewLabel((Transform)(object)val, "", num3 - 60f, 20f, (0f - num3) / 2f + 58f + (num3 - 60f) / 2f, num5 - 10f, 11, ColDim, (TextAnchor)3);
		num5 -= 34f;
		string[] array = new string[8] { "作者", "对象", "大小", "时长", "创建", "修改", "版本", "类型" };
		for (int i = 0; i < array.Length; i++)
		{
			float y2 = num5 - 11f;
			NewLabel((Transform)(object)val, array[i], 52f, 22f, (0f - num3) / 2f + 26f, y2, 12, ColDim, (TextAnchor)3);
			Text item = NewLabel((Transform)(object)val, "", num3 - 66f, 22f, (0f - num3) / 2f + 66f + (num3 - 66f) / 2f, y2, 12, ColSub, (TextAnchor)3);
			_detailVals.Add(item);
			num5 -= 24f;
		}
		num5 -= 8f;
		NewRect("DSep", (Transform)(object)val, num3, 1f, 0f, num5 - 2f, ColLine, raycast: false);
		num5 -= 6f;
		_detailPath = NewLabel((Transform)(object)val, "", num3, 34f, 0f, num5 - 17f, 10, ColDim, (TextAnchor)0);
		_detailPath.alignment = (TextAnchor)0;
		((Object)_detailPath).name = "DPath";
		_detailPath.horizontalOverflow = (HorizontalWrapMode)0;
		_detailPath.verticalOverflow = (VerticalWrapMode)0;
		num5 -= 42f;
		Button val4 = NewButton((Transform)(object)val, "同目录 0 张", num3, 30f, 0f, num5 - 15f, new UnityAction(OnClickSameDir));
		((Object)val4).name = "Btn_SameDir";
		_detailSameDirText = ((Component)((Component)val4).transform.Find("Label")).GetComponent<Text>();
		num5 -= 40f;
		Button val5 = NewButton((Transform)(object)val, "读取此卡", num3, 40f, 0f, num5 - 20f, new UnityAction(OnClickLoad));
		PaintButton(val5, ColAccent, Color.white);
		num5 -= 50f;
		float num6 = (num3 - 8f) / 2f;
		Button impBtn2 = NewButton((Transform)(object)val, "导入", num6, 34f, (0f - num3) / 2f + num6 / 2f, num5 - 17f, new UnityAction(OnClickImport));
		PaintButton(impBtn2, ColField, ColSub);
		Button val6 = NewButton((Transform)(object)val, "★ 收藏", num6, 34f, num3 / 2f - num6 / 2f, num5 - 17f, new UnityAction(OnToggleFav));
		PaintButton(val6, ColField, ColGold);
		_detailFavText = ((Component)((Component)val6).transform.Find("Label")).GetComponent<Text>();
		_detailGo.SetActive(false);
	}

	private static string FormatDur(float s)
	{
		if (s >= 60f)
		{
			int num = (int)(s / 60f);
			float num2 = s - (float)num * 60f;
			return num + " 分 " + num2.ToString("0.0", CultureInfo.InvariantCulture) + " 秒";
		}
		return s.ToString("0.0", CultureInfo.InvariantCulture) + " 秒";
	}

	private int DirCount(string dir)
	{
		if (string.IsNullOrEmpty(dir))
		{
			return 0;
		}
		int num = 0;
		lock (_all)
		{
			for (int i = 0; i < _all.Count; i++)
			{
				string directoryName = Path.GetDirectoryName(_all[i].Path);
				if (string.Equals(directoryName, dir, StringComparison.OrdinalIgnoreCase))
				{
					num++;
				}
			}
			return num;
		}
	}

	private void OnClickSameDir()
	{
		if (_selected >= 0 && _selected < _view.Count)
		{
			Entry entry = _view[_selected];
			_author = entry.Author ?? "";
			_sub = entry.Sub ?? "";
			_expanded.Add(AuthorKey(_author));
			_sideDirty = true;
			RefreshView();
		}
	}

	private void OnThumbSlider(float t)
	{
		int num = Mathf.Clamp(4 + Mathf.RoundToInt(t * 6f), 4, 10);
		if ((Object)(object)_sliderKnob != (Object)null && (Object)(object)_sliderTrack != (Object)null)
		{
			float trackLeft = _sliderTrack.anchoredPosition.x - _sliderW / 2f;
			_sliderKnob.anchoredPosition = new Vector2(KnobX(trackLeft, num), _sliderKnob.anchoredPosition.y);
		}
		if ((Object)(object)_sliderFill != (Object)null)
		{
			_sliderFill.sizeDelta = new Vector2(SliderFillW(num), 6f);
		}
		if (num != _colCount)
		{
			_colCount = num;
			if (_colCountCfg != null)
			{
				_colCountCfg.Value = num;
			}
			RefreshView();
		}
	}

	private string ChipLabel(int i)
	{
		return i switch
		{
			1 => "★ 收藏", 
			2 => "最近", 
			3 => "版本：" + VerNames[Mathf.Clamp(_verMode, 0, VerNames.Length - 1)], 
			4 => "大小：" + SizeNames[Mathf.Clamp(_sizeMode, 0, SizeNames.Length - 1)], 
			5 => "时长：" + ((_durMode == 6) ? DurRangeText() : DurNames[Mathf.Clamp(_durMode, 0, DurNames.Length - 1)]), 
			6 => "分类：" + (string.IsNullOrEmpty(_collFilter) ? "全部" : _collFilter), 
			_ => "全部", 
		};
	}

	private bool ChipOn(int i)
	{
		switch (i)
		{
		case 1:
			return _chip == 1;
		case 2:
			return _chip == 2;
		case 3:
			return _verMode != 0;
		case 4:
			return _sizeMode != 0;
		case 5:
			return _durMode != 0;
		case 6:
			return !string.IsNullOrEmpty(_collFilter);
		default:
			if (_chip == 0 && _verMode == 0 && _sizeMode == 0 && _durMode == 0)
			{
				return string.IsNullOrEmpty(_collFilter);
			}
			return false;
		}
	}

	private string[] DropItems(int owner)
	{
		switch (owner)
		{
		case 6:
			return new string[6] { "修改时间", "创建日期", "名称", "大小", "版本", "时长" };
		case 3:
			return new string[3] { "全部版本", "KKS（1.1.x）", "KK（1.0.x）" };
		case 4:
			return new string[5] { "全部大小", "小    < 5 MB", "中    5–20 MB", "大    20–50 MB", "特大  > 50 MB" };
		case 5:
			return new string[7] { "全部时长", "极短  < 10 秒", "短    10–15 秒", "中    15–30 秒", "长    30–60 秒", "超长  > 60 秒", "自定义…（秒）" };
		case 7:
		{
			List<string> list = new List<string>();
			list.Add("全部卡片");
			list.Add("＋ 新建分类…");
			for (int i = 0; i < _colls.Count; i++)
			{
				list.Add(_colls[i].Name + "    (" + _colls[i].Paths.Count + ")");
			}
			return list.ToArray();
		}
		default:
			return null;
		}
	}

	private void OnChipClicked(int i)
	{
		if (i >= 3)
		{
			int num = ((i == 6) ? 7 : i);
			if ((Object)(object)_dropGo != (Object)null && _dropOwner == num)
			{
				CloseDrop();
			}
			else if (num == 7)
			{
				ShowDrop(7, (i < _chipX.Count) ? _chipX[i] : 0f, _chipY - 19f);
			}
			else
			{
				ShowDrop(num);
			}
			return;
		}
		if (i == 0)
		{
			_verMode = 0;
			_sizeMode = 0;
			_durMode = 0;
			_collFilter = null;
		}
		_chip = i;
		CloseDrop();
		UpdateToggleVisuals();
		RefreshView();
	}

	private void OnSortClicked()
	{
		if ((Object)(object)_dropGo != (Object)null && _dropOwner == 6)
		{
			CloseDrop();
		}
		else
		{
			ShowDrop(6, _sortCx, _sortY - 23f);
		}
	}

	private void ShowDrop(int owner)
	{
		float cxp = ((owner < _chipX.Count) ? _chipX[owner] : 0f);
		ShowDrop(owner, cxp, _chipY - 19f);
	}

	private void ShowDrop(int owner, float cxp, float topY)
	{
		CloseDrop();
		string[] array = DropItems(owner);
		if (array == null || array.Length == 0)
		{
			return;
		}
		_dropOwner = owner;
		int num = owner switch
		{
			3 => _verMode, 
			4 => _sizeMode, 
			5 => _durMode, 
			6 => _sortMode, 
			7 => CollFilterIndex(), 
			_ => 0, 
		};
		float num2 = ((owner == 7) ? 214f : 178f);
		float num3 = 10f + 32f * (float)array.Length;
		float y = topY - num3 / 2f;
		RectTransform val = NewRound("ChipDrop", (Transform)(object)_rootRect, num2, num3, cxp, y, UiTheme.SurfaceAlt, RCard, true);
		_dropGo = ((Component)val).gameObject;
		_dropFrame = Time.frameCount;
		_dropBtns.Clear();
		_dropTexts.Clear();
		for (int i = 0; i < array.Length; i++)
		{
			int ii = i;
			float y2 = num3 / 2f - 5f - 16f - (float)i * 32f;
			Button val2 = NewButton((Transform)(object)val, array[i], num2 - 12f, 30f, 0f, y2, (UnityAction)delegate
			{
				OnDropPick(ii);
			});
			((Object)val2).name = "Drop" + i;
			Image component = ((Component)val2).GetComponent<Image>();
			if ((Object)(object)component != (Object)null)
			{
				((Graphic)component).color = (Color)((i == num) ? ColAccent : new Color(0f, 0f, 0f, 0f));
			}
			Text component2 = ((Component)((Component)val2).transform.Find("Label")).GetComponent<Text>();
			if ((Object)(object)component2 != (Object)null)
			{
				component2.alignment = (TextAnchor)3;
				((Graphic)component2).color = (Color)((i == num) ? Color.white : UiTheme.Text);
				RectTransform component3 = ((Component)component2).GetComponent<RectTransform>();
				if ((Object)(object)component3 != (Object)null)
				{
					component3.sizeDelta = new Vector2(num2 - 34f, 30f);
				}
				if (owner == 7)
				{
					component2.text = Clip(array[i], 16);
				}
			}
			_dropBtns.Add(val2);
			_dropTexts.Add(component2);
		}
	}

	private void OnDropPick(int idx)
	{
		int dropOwner = _dropOwner;
		CloseDrop();
		switch (dropOwner)
		{
		case 6:
			_sortMode = Mathf.Clamp(idx, 0, 5);
			UpdateSortButtonVisuals();
			RefreshView(keepPos: true);
			return;
		case 7:
			switch (idx)
			{
			case 0:
				_collFilter = null;
				break;
			case 1:
				ShowInput("新建分类", "", delegate(string v)
				{
					AddCollection(v);
				});
				return;
			default:
			{
				int num = idx - 2;
				if (num >= 0 && num < _colls.Count)
				{
					_collFilter = _colls[num].Name;
				}
				break;
			}
			}
			UpdateToggleVisuals();
			RefreshView();
			return;
		case 5:
			if (idx == 6)
			{
				ShowInput("自定义时长（秒）", (_durMode == 6) ? DurRangeInit() : "", ApplyCustomDur, "例：15-45  20-  -40");
				return;
			}
			break;
		}
		switch (dropOwner)
		{
		case 3:
			_verMode = idx;
			break;
		case 4:
			_sizeMode = idx;
			break;
		case 5:
			_durMode = idx;
			break;
		default:
			return;
		}
		UpdateToggleVisuals();
		RefreshView();
	}

	private static bool ParseDurRange(string raw, out float lo, out float hi)
	{
		lo = 0f;
		hi = 0f;
		if (string.IsNullOrEmpty(raw))
		{
			return false;
		}
		string text = raw.Replace(" ", "").Replace("\u3000", "").Replace("～", "-")
			.Replace("~", "-")
			.Replace("—", "-")
			.Replace("－", "-")
			.Replace("到", "-")
			.Replace("秒", "");
		if (text.Length == 0)
		{
			return false;
		}
		int num = text.IndexOf('-');
		if (num < 0)
		{
			if (!float.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var result))
			{
				return false;
			}
			if (result <= 0f)
			{
				return false;
			}
			lo = result;
			return true;
		}
		string text2 = text.Substring(0, num);
		string text3 = text.Substring(num + 1);
		bool flag = text2.Length > 0;
		bool flag2 = text3.Length > 0;
		if (!flag && !flag2)
		{
			return false;
		}
		float result2 = 0f;
		float result3 = 0f;
		if (flag && !float.TryParse(text2, NumberStyles.Float, CultureInfo.InvariantCulture, out result2))
		{
			return false;
		}
		if (flag2 && !float.TryParse(text3, NumberStyles.Float, CultureInfo.InvariantCulture, out result3))
		{
			return false;
		}
		if (flag)
		{
			lo = Mathf.Max(0f, result2);
		}
		if (flag2)
		{
			hi = Mathf.Max(0f, result3);
		}
		if (lo > 0f && hi > 0f && hi < lo)
		{
			float num2 = lo;
			lo = hi;
			hi = num2;
		}
		if (!(lo > 0f))
		{
			return hi > 0f;
		}
		return true;
	}

	private static string DurShort(float sec)
	{
		if (sec >= 60f)
		{
			float num = sec / 60f;
			return num.ToString((num == Mathf.Floor(num)) ? "0" : "0.#", CultureInfo.InvariantCulture) + "m";
		}
		return sec.ToString((sec == Mathf.Floor(sec)) ? "0" : "0.#", CultureInfo.InvariantCulture) + "s";
	}

	private string DurRangeText()
	{
		if (_durMin > 0f && _durMax > 0f)
		{
			return DurShort(_durMin) + "–" + DurShort(_durMax);
		}
		if (_durMin > 0f)
		{
			return "> " + DurShort(_durMin);
		}
		if (_durMax > 0f)
		{
			return "< " + DurShort(_durMax);
		}
		return "自定义";
	}

	private string DurRangeInit()
	{
		string text = ((_durMin > 0f) ? _durMin.ToString("0.#", CultureInfo.InvariantCulture) : "");
		string text2 = ((_durMax > 0f) ? _durMax.ToString("0.#", CultureInfo.InvariantCulture) : "");
		return text + "-" + text2;
	}

	private void ApplyCustomDur(string v)
	{
		if (!ParseDurRange(v, out var lo, out var hi))
		{
			Log.LogWarning((object)("dur range not understood: " + v));
			return;
		}
		_durMin = lo;
		_durMax = hi;
		_durMode = 6;
		SaveLastPos();
		UpdateToggleVisuals();
		RefreshView();
		Log.LogInfo((object)("dur range: " + DurRangeText()));
	}

	private static bool MouseInRect(GameObject go)
	{
		if ((Object)(object)go == (Object)null)
		{
			return false;
		}
		RectTransform component = go.GetComponent<RectTransform>();
		if ((Object)(object)component == (Object)null)
		{
			return false;
		}
		Vector2 val = default(Vector2);
		val = new Vector2(Input.mousePosition.x, Input.mousePosition.y);
		return RectTransformUtility.RectangleContainsScreenPoint(component, val, (Camera)null);
	}

	private bool MouseInAnyDropTrigger(Vector2 mp)
	{
		for (int i = 0; i < _chipBtns.Count; i++)
		{
			if (!((Object)(object)_chipBtns[i] == (Object)null))
			{
				RectTransform component = ((Component)_chipBtns[i]).GetComponent<RectTransform>();
				if ((Object)(object)component != (Object)null && RectTransformUtility.RectangleContainsScreenPoint(component, mp, (Camera)null))
				{
					return true;
				}
			}
		}
		if ((Object)(object)_sortBtn != (Object)null)
		{
			RectTransform component2 = ((Component)_sortBtn).GetComponent<RectTransform>();
			if ((Object)(object)component2 != (Object)null && RectTransformUtility.RectangleContainsScreenPoint(component2, mp, (Camera)null))
			{
				return true;
			}
		}
		return false;
	}

	private void CloseDrop()
	{
		if ((Object)(object)_dropGo != (Object)null)
		{
			Object.Destroy((Object)(object)_dropGo);
			_dropGo = null;
		}
		_dropOwner = -1;
		_dropBtns.Clear();
		_dropTexts.Clear();
	}

	private void OnCycleSort()
	{
		_sortMode = (_sortMode + 1) % 6;
		UpdateSortButtonVisuals();
		RefreshView();
	}

	private void OnToggleOrder()
	{
		_sortDesc = !_sortDesc;
		UpdateSortButtonVisuals();
		RefreshView();
	}

	private void UpdateOrderVisual()
	{
		if (!((Object)(object)_orderBtnText == (Object)null))
		{
			string text = (_sortDesc ? "↓" : "↑");
			if (_orderBtnText.text != text)
			{
				_orderBtnText.text = text;
			}
			((Graphic)_orderBtnText).color = (_sortDesc ? ColSub : ColAccent);
		}
	}

	private void OnToggleFav()
	{
		if (_selected >= 0 && _selected < _view.Count)
		{
			ToggleFav(_view[_selected].Path);
		}
	}

	private void ToggleFav(string path)
	{
		if (path != null)
		{
			if (!_favs.Remove(path))
			{
				_favs.Add(path);
			}
			SaveFavs();
			UpdateToggleVisuals();
			RefreshView(keepPos: true);
		}
	}

	private void UpdateToggleVisuals()
	{
		for (int i = 0; i < _chipBtns.Count; i++)
		{
			bool flag = ChipOn(i);
			Image component = ((Component)_chipBtns[i]).GetComponent<Image>();
			if ((Object)(object)component != (Object)null)
			{
				((Graphic)component).color = (flag ? ColAccent : ColField);
			}
			if (i < _chipTexts.Count && (Object)(object)_chipTexts[i] != (Object)null)
			{
				((Graphic)_chipTexts[i]).color = (Color)(flag ? Color.white : UiTheme.TextSub);
				RectTransform component2 = ((Component)_chipBtns[i]).GetComponent<RectTransform>();
				float num = (((Object)(object)component2 != (Object)null) ? component2.sizeDelta.x : 120f);
				int budget = Mathf.Max(3, Mathf.FloorToInt((num - 12f) / 7f));
				string text = "<color=#FFFFFF00><size=1>\u180e</size></color>" + Clip(ChipLabel(i), budget);
				if (_chipTexts[i].text != text)
				{
					_chipTexts[i].text = text;
				}
			}
		}
		UpdateSideSelection();
	}

	private void UpdateSortButtonVisuals()
	{
		if (!((Object)(object)_sortBtnText == (Object)null))
		{
			string[] array = new string[6] { "修改时间", "创建日期", "名称", "大小", "版本", "时长" };
			string text = "<color=#FFFFFF00><size=1>\u180e</size></color>" + array[Mathf.Clamp(_sortMode, 0, 5)] + "  ▼";
			if (_sortBtnText.text != text)
			{
				_sortBtnText.text = text;
			}
			UpdateOrderVisual();
		}
	}

	private static string AuthorKey(string a)
	{
		return "A:" + (string.IsNullOrEmpty(a) ? "\u0001" : a);
	}

	private static string NodeKey(string a, string path)
	{
		return "N:" + (string.IsNullOrEmpty(a) ? "\u0001" : a) + "\u0001" + path;
	}

	private TreeNode BuildAuthorTree(string author)
	{
		TreeNode treeNode = new TreeNode();
		treeNode.Name = "";
		treeNode.Path = "";
		Dictionary<string, TreeNode> dictionary = new Dictionary<string, TreeNode>(StringComparer.OrdinalIgnoreCase);
		dictionary[""] = treeNode;
		lock (_all)
		{
			for (int i = 0; i < _all.Count; i++)
			{
				Entry entry = _all[i];
				if (!string.Equals(entry.Author ?? "", author ?? "", StringComparison.OrdinalIgnoreCase))
				{
					continue;
				}
				string text = entry.Sub ?? "";
				treeNode.Count++;
				if (text.Length == 0)
				{
					treeNode.Direct++;
					continue;
				}
				string[] array = text.Split('/');
				string text2 = "";
				TreeNode treeNode2 = treeNode;
				for (int j = 0; j < array.Length; j++)
				{
					if (array[j].Length != 0)
					{
						text2 = ((text2.Length == 0) ? array[j] : (text2 + "/" + array[j]));
						if (!dictionary.TryGetValue(text2, out var value))
						{
							value = new TreeNode();
							value.Name = array[j];
							value.Path = text2;
							dictionary[text2] = value;
							treeNode2.Children.Add(value);
						}
						value.Count++;
						treeNode2 = value;
					}
				}
				treeNode2.Direct++;
			}
		}
		SortTree(treeNode);
		return treeNode;
	}

	private static void SortTree(TreeNode n)
	{
		n.Children.Sort((TreeNode a, TreeNode b) => string.Compare(a.Name, b.Name, StringComparison.CurrentCultureIgnoreCase));
		for (int i = 0; i < n.Children.Count; i++)
		{
			SortTree(n.Children[i]);
		}
	}

	private void FlattenNode(TreeNode n, int depth, string author, string root, List<SideRow> rows)
	{
		for (int i = 0; i < n.Children.Count; i++)
		{
			TreeNode treeNode = n.Children[i];
			SideRow sideRow = new SideRow();
			sideRow.Label = treeNode.Name;
			sideRow.Count = treeNode.Count;
			sideRow.Depth = depth;
			sideRow.HasChildren = treeNode.Children.Count > 0;
			sideRow.Key = NodeKey(author, treeNode.Path);
			sideRow.Dir = NodeDir(root, author, treeNode.Path);
			sideRow.Expanded = _expanded.Contains(sideRow.Key);
			string a2 = author;
			string p2 = treeNode.Path;
			sideRow.OnClick = (UnityAction)delegate
			{
				PickNodeToggle(a2, p2);
			};
			if (sideRow.HasChildren)
			{
				string k2 = sideRow.Key;
				sideRow.OnToggle = (UnityAction)delegate
				{
					ToggleExpand(k2);
				};
			}
			rows.Add(sideRow);
			if (sideRow.Expanded && treeNode.Children.Count > 0)
			{
				FlattenNode(treeNode, depth + 1, author, root, rows);
			}
		}
	}

	private void RebuildSideList()
	{
		if ((Object)(object)_sideContent == (Object)null)
		{
			return;
		}
		for (int i = 0; i < _sideRows.Count; i++)
		{
			if ((Object)(object)_sideRows[i] != (Object)null)
			{
				Object.Destroy((Object)(object)((Component)_sideRows[i]).gameObject);
			}
		}
		_sideRows.Clear();
		_sideRowTexts.Clear();
		_sideRowCounts.Clear();
		_sideSelBgs.Clear();
		_sideKeys.Clear();
		List<SideRow> list = new List<SideRow>();
		string root = SceneRoot();
		SideRow sideRow = new SideRow();
		sideRow.Label = "全部作者";
		sideRow.Count = AllCount();
		sideRow.Key = "A:";
		sideRow.OnClick = (UnityAction)delegate
		{
			PickAllAuthors();
		};
		list.Add(sideRow);
		List<KeyValuePair<string, int>> list2 = AuthorItems();
		for (int j = 0; j < list2.Count; j++)
		{
			string key = list2[j].Key;
			TreeNode treeNode = BuildAuthorTree(key);
			SideRow sideRow2 = new SideRow();
			sideRow2.Label = ((key.Length == 0) ? "未分类" : key);
			sideRow2.Count = list2[j].Value;
			sideRow2.Depth = 0;
			sideRow2.HasChildren = treeNode.Children.Count > 0;
			sideRow2.Key = AuthorKey(key);
			sideRow2.Dir = NodeDir(root, key, "");
			sideRow2.Expanded = _expanded.Contains(sideRow2.Key);
			string a2 = key;
			sideRow2.OnClick = (UnityAction)delegate
			{
				PickAuthorToggle(a2);
			};
			if (sideRow2.HasChildren)
			{
				string k2 = sideRow2.Key;
				sideRow2.OnToggle = (UnityAction)delegate
				{
					ToggleExpand(k2);
				};
			}
			list.Add(sideRow2);
			if (!sideRow2.Expanded || treeNode.Children.Count <= 0)
			{
				continue;
			}
			if (treeNode.Direct > 0)
			{
				SideRow sideRow3 = new SideRow();
				sideRow3.Label = "（根目录）";
				sideRow3.Count = treeNode.Direct;
				sideRow3.Depth = 1;
				sideRow3.Key = NodeKey(key, "");
				sideRow3.Dir = NodeDir(root, key, "");
				string a3 = key;
				sideRow3.OnClick = (UnityAction)delegate
				{
					PickNode(a3, "");
				};
				list.Add(sideRow3);
			}
			FlattenNode(treeNode, 1, key, root, list);
		}
		if ((Object)(object)_sideHead != (Object)null)
		{
			_sideHead.text = "<color=#FFFFFF00><size=1>\u180e</size></color>作者 / 目录";
		}
		float num = 192f;
		float num2 = 26f;
		float num3 = (_sideContentH = (float)list.Count * num2 + 8f);
		_sideContent.sizeDelta = new Vector2(num, num3);
		for (int l = 0; l < list.Count; l++)
		{
			SideRow sideRow4 = list[l];
			float num4 = -4f - (float)l * num2;
			Button val = NewButton((Transform)(object)_sideContent, "", num - 6f, num2 - 2f, 0f, 0f, null);
			RectTransform component = ((Component)val).GetComponent<RectTransform>();
			component.anchorMin = new Vector2(0.5f, 1f);
			component.anchorMax = new Vector2(0.5f, 1f);
			component.pivot = new Vector2(0.5f, 1f);
			component.anchoredPosition = new Vector2(0f, num4);
			SideRow rowRef = sideRow4;
			((UnityEvent)val.onClick).AddListener((UnityAction)delegate
			{
				OnSideRowClick(rowRef);
			});
			Image component2 = ((Component)val).GetComponent<Image>();
			if ((Object)(object)component2 != (Object)null)
			{
				((Graphic)component2).color = new Color(0f, 0f, 0f, 0f);
			}
			int num5 = Mathf.Min(sideRow4.Depth, 5);
			float num6 = num - 6f;
			float num7 = num6 / 2f;
			float indent = 11f;
			float slotLeft = (0f - num7) + 3f + (float)num5 * indent;
			bool hasToggle = sideRow4.OnToggle != null;
			float textLeft = slotLeft + (hasToggle ? 12f : 2f);
			float selLeft = slotLeft - 2f;
			float selRight = num7 - 4f;
			RectTransform selBg = NewRound("Sel", ((Component)val).transform, Mathf.Max(20f, selRight - selLeft), 20f, (selLeft + selRight) / 2f, 0f, ColSideSel, RItem, false);
			Image selImg = ((Component)selBg).GetComponent<Image>();
			if ((Object)(object)selImg != (Object)null)
			{
				((Graphic)selImg).color = new Color(0f, 0f, 0f, 0f);
			}
			if (hasToggle)
			{
				Text val2 = NewLabel(((Component)val).transform, sideRow4.Expanded ? "▼" : "►", 12f, 16f, slotLeft + 6f, 0f, 9, ColDim, (TextAnchor)4);
				((Object)val2).name = "Arrow";
			}
			Text component3 = ((Component)((Component)val).transform.Find("Label")).GetComponent<Text>();
			if ((Object)(object)component3 != (Object)null)
			{
				float textRight = num7 - ((sideRow4.Count >= 0) ? 50f : 8f);
				float num9 = Mathf.Max(12f, textRight - textLeft);
				int budget = Mathf.Max(3, Mathf.FloorToInt(num9 / 6f));
				component3.text = "<color=#FFFFFF00><size=1>\u180e</size></color>" + Clip(sideRow4.Label, budget);
				component3.alignment = (TextAnchor)3;
				component3.fontSize = 12;
				((Graphic)component3).color = ColSideRow;
				RectTransform component4 = ((Component)component3).GetComponent<RectTransform>();
				component4.sizeDelta = new Vector2(num9, 22f);
				component4.anchoredPosition = new Vector2(textLeft + num9 / 2f, 0f);
				((Component)component3).transform.SetAsLastSibling();
			}
			Text val3 = null;
			if (sideRow4.Count >= 0)
			{
				val3 = NewLabel(((Component)val).transform, sideRow4.Count.ToString(), 46f, num2 - 2f, 0f, 0f, 11, ColDim, (TextAnchor)5);
				RectTransform component5 = ((Component)val3).GetComponent<RectTransform>();
				component5.anchoredPosition = new Vector2(num6 / 2f - 27f, 0f);
			}
			_sideRows.Add(val);
			_sideRowTexts.Add(component3);
			_sideRowCounts.Add(val3);
			_sideSelBgs.Add(selImg);
			_sideKeys.Add(sideRow4.Key);
		}
		float num10 = Mathf.Max(0f, _sideContentH - _sideViewH);
		if (_sideScrollY > num10)
		{
			_sideScrollY = num10;
		}
		if (_sideScrollY < 0f)
		{
			_sideScrollY = 0f;
		}
		_sideContent.anchoredPosition = new Vector2(0f, _sideScrollY);
		UpdateSideSelection();
	}

	private string CurrentSideKey()
	{
		if (_author == null)
		{
			return "A:";
		}
		if (_sub == null)
		{
			return AuthorKey(_author);
		}
		return NodeKey(_author, _sub);
	}

	private void UpdateSideSelection()
	{
		if (_sideRows.Count == 0)
		{
			return;
		}
		string b = CurrentSideKey();
		for (int i = 0; i < _sideRows.Count && i < _sideKeys.Count; i++)
		{
			if (!((Object)(object)_sideRows[i] == (Object)null))
			{
				string text = _sideKeys[i];
				bool flag = text != null && string.Equals(text, b, StringComparison.OrdinalIgnoreCase);
				if (i < _sideSelBgs.Count && (Object)(object)_sideSelBgs[i] != (Object)null)
				{
					((Graphic)_sideSelBgs[i]).color = (Color)(flag ? ColSideSel : new Color(0f, 0f, 0f, 0f));
				}
				if (i < _sideRowTexts.Count && (Object)(object)_sideRowTexts[i] != (Object)null)
				{
					((Graphic)_sideRowTexts[i]).color = (flag ? ColText : ColSideRow);
				}
				if (i < _sideRowCounts.Count && (Object)(object)_sideRowCounts[i] != (Object)null)
				{
					((Graphic)_sideRowCounts[i]).color = (flag ? ColAccent : ColDim);
				}

			}
		}
	}

	private void OnSideWheel(float dy)
	{
		_sideScrollY -= dy * 60f;
		float num = Mathf.Max(0f, _sideContentH - _sideViewH);
		_sideScrollY = Mathf.Clamp(_sideScrollY, 0f, num);
		if ((Object)(object)_sideContent != (Object)null)
		{
			_sideContent.anchoredPosition = new Vector2(0f, _sideScrollY);
		}
	}

	private void ToggleExpand(string key)
	{
		if (key != null)
		{
			if (!_expanded.Remove(key))
			{
				_expanded.Add(key);
			}
			_sideDirty = true;
		}
	}

	private void PickAllAuthors()
	{
		_author = null;
		_sub = null;
		_sideDirty = true;
		_sideScrollY = 0f;
		RefreshView();
	}

	private void PickAuthor(string name)
	{
		_author = name;
		_sub = null;
		_expanded.Add(AuthorKey(name));
		_sideDirty = true;
		RefreshView();
	}

	private void PickAuthorToggle(string name)
	{
		_author = name;
		_sub = null;
		_sideDirty = true;
		string item = AuthorKey(name);
		if (!_expanded.Remove(item))
		{
			_expanded.Add(item);
		}
		RefreshView();
	}

	private void PickNodeToggle(string author, string path)
	{
		_author = author;
		_sub = path;
		_sideDirty = true;
		string item = NodeKey(author, path);
		if (!_expanded.Remove(item))
		{
			_expanded.Add(item);
		}
		RefreshView();
	}

	private void PickNode(string author, string path)
	{
		_author = author;
		_sub = path;
		_expanded.Add(AuthorKey(author));
		_sideDirty = true;
		RefreshView();
	}

	private int CountOf(string author)
	{
		if (author == null)
		{
			return AllCount();
		}
		int num = 0;
		lock (_all)
		{
			for (int i = 0; i < _all.Count; i++)
			{
				if (string.Equals(_all[i].Author ?? "", author, StringComparison.OrdinalIgnoreCase))
				{
					num++;
				}
			}
			return num;
		}
	}

	private List<KeyValuePair<string, int>> AuthorItems()
	{
		Dictionary<string, int> dictionary = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
		lock (_all)
		{
			for (int i = 0; i < _all.Count; i++)
			{
				string key = _all[i].Author ?? "";
				if (dictionary.TryGetValue(key, out var value))
				{
					dictionary[key] = value + 1;
				}
				else
				{
					dictionary[key] = 1;
				}
			}
		}
		List<string> list = new List<string>(dictionary.Keys);
		list.Sort(delegate(string x, string y)
		{
			bool flag = string.IsNullOrEmpty(x);
			bool flag2 = string.IsNullOrEmpty(y);
			return (flag != flag2) ? (flag ? 1 : (-1)) : string.Compare(x, y, StringComparison.CurrentCultureIgnoreCase);
		});
		List<KeyValuePair<string, int>> list2 = new List<KeyValuePair<string, int>>();
		for (int j = 0; j < list.Count; j++)
		{
			list2.Add(new KeyValuePair<string, int>(list[j], dictionary[list[j]]));
		}
		return list2;
	}

	private string IndexFile()
	{
		return Path.Combine(Paths.ConfigPath, "kk.scene.browser.index.txt");
	}

	private void IndexLoad()
	{
		try
		{
			string path = IndexFile();
			if (!File.Exists(path))
			{
				return;
			}
			string[] array = File.ReadAllLines(path);
			int num = 0;
			foreach (string text in array)
			{
				if (text.Length == 0 || text[0] == '#')
				{
					continue;
				}
				string[] array2 = text.Split('\t');
				if (array2.Length < 6)
				{
					num++;
					continue;
				}
				if (!long.TryParse(array2[1], out var result))
				{
					num++;
					continue;
				}
				if (!long.TryParse(array2[2], out var result2))
				{
					num++;
					continue;
				}
				IdxRec idxRec = new IdxRec();
				idxRec.Size = result;
				idxRec.MTicks = result2;
				idxRec.Ver = array2[3];
				if (!int.TryParse(array2[4], out var result3))
				{
					num++;
					continue;
				}
				idxRec.Obj = result3;
				if (!int.TryParse(array2[5], out result3))
				{
					num++;
					continue;
				}
				idxRec.VerParsed = result3;
				float result4 = 0f;
				if (array2.Length >= 7)
				{
					float.TryParse(array2[6], NumberStyles.Float, CultureInfo.InvariantCulture, out result4);
				}
				idxRec.Dur = result4;
				int result5 = 0;
				if (array2.Length >= 8)
				{
					int.TryParse(array2[7], out result5);
				}
				idxRec.DurParsed = result5;
				_idx[array2[0]] = idxRec;
			}
			Log.LogInfo((object)("index cache loaded: " + _idx.Count + "  bad=" + num));
		}
		catch (Exception ex)
		{
			Log.LogWarning((object)("index cache load: " + ex.Message));
		}
	}

	private void IndexSave()
	{
		try
		{
			List<Entry> list;
			lock (_all)
			{
				list = new List<Entry>(_all);
			}
			List<string> list2 = new List<string>(list.Count + 1);
			list2.Add("# KK Scene Browser 索引缓存 —— 删掉本文件只会让下次重扫慢一点");
			for (int i = 0; i < list.Count; i++)
			{
				Entry entry = list[i];
				if (entry.VerParsed != 0)
				{
					list2.Add(entry.Path + "\t" + entry.Size + "\t" + entry.MTime.Ticks + "\t" + entry.Ver + "\t" + entry.ObjCount + "\t" + entry.VerParsed + "\t" + entry.Dur.ToString(CultureInfo.InvariantCulture) + "\t" + entry.DurParsed);
				}
			}
			File.WriteAllLines(IndexFile(), list2.ToArray());
			Log.LogInfo((object)("index cache saved: " + (list2.Count - 1)));
		}
		catch (Exception ex)
		{
			Log.LogWarning((object)("index cache save: " + ex.Message));
		}
	}

	private string FavFile()
	{
		try
		{
			return Path.Combine(Paths.ConfigPath, "kk.scene.browser.favs.txt");
		}
		catch (Exception)
		{
			return null;
		}
	}

	private void LoadFavs()
	{
		try
		{
			string text = FavFile();
			if (text == null || !File.Exists(text))
			{
				return;
			}
			string[] array = File.ReadAllLines(text);
			for (int i = 0; i < array.Length; i++)
			{
				string text2 = array[i].Trim();
				if (text2.Length > 0)
				{
					_favs.Add(text2);
				}
			}
			Log.LogInfo((object)("favs loaded: " + _favs.Count));
		}
		catch (Exception ex)
		{
			Log.LogError((object)("favs load: " + ex));
		}
	}

	private void SaveFavs()
	{
		try
		{
			string text = FavFile();
			if (text != null)
			{
				string[] array = new string[_favs.Count];
				_favs.CopyTo(array);
				Array.Sort(array, StringComparer.OrdinalIgnoreCase);
				File.WriteAllLines(text, array);
			}
		}
		catch (Exception ex)
		{
			Log.LogError((object)("favs save: " + ex));
		}
	}

	private string StateFile()
	{
		try
		{
			return Path.Combine(Paths.ConfigPath, "kk.scene.browser.state.txt");
		}
		catch (Exception)
		{
			return null;
		}
	}

	private void LoadLastPos()
	{
		try
		{
			string text = StateFile();
			if (text != null && File.Exists(text))
			{
				string[] array = File.ReadAllLines(text);
				if (array.Length != 0 && array[0].Trim().Length > 0)
				{
					_lastAuthor = array[0].Trim();
				}
				if (array.Length > 1 && array[1].Trim().Length > 0)
				{
					_lastSub = array[1].Trim();
				}
				if (array.Length > 2)
				{
					int.TryParse(array[2].Trim(), out _durMode);
				}
				if (array.Length > 3)
				{
					float.TryParse(array[3].Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out _durMin);
				}
				if (array.Length > 4)
				{
					float.TryParse(array[4].Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out _durMax);
				}
				Log.LogInfo((object)("last pos: " + (_lastAuthor ?? "-") + " / " + (_lastSub ?? "-")));
			}
		}
		catch (Exception ex)
		{
			Log.LogError((object)("state load: " + ex));
		}
	}

	private void ApplyLastPos()
	{
		if (_lastPosApplied)
		{
			return;
		}
		_lastPosApplied = true;
		if (!string.IsNullOrEmpty(_lastAuthor))
		{
			_author = _lastAuthor;
			_sub = (string.IsNullOrEmpty(_lastSub) ? null : _lastSub);
			_expanded.Add(AuthorKey(_author));
			if (_sub != null)
			{
				_expanded.Add(NodeKey(_author, _sub));
			}
			_sideDirty = true;
			Log.LogInfo((object)("restore pos: " + _author + " / " + (_sub ?? "-")));
		}
	}

	private void SaveLastPos()
	{
		try
		{
			string text = StateFile();
			if (text != null)
			{
				File.WriteAllLines(text, new string[5]
				{
					_author ?? "",
					_sub ?? "",
					_durMode.ToString(),
					_durMin.ToString(CultureInfo.InvariantCulture),
					_durMax.ToString(CultureInfo.InvariantCulture)
				});
			}
		}
		catch (Exception)
		{
		}
	}

	private void AddRecent(string path)
	{
		if (!string.IsNullOrEmpty(path))
		{
			_recent.Remove(path);
			_recent.Insert(0, path);
			while (_recent.Count > 40)
			{
				_recent.RemoveAt(_recent.Count - 1);
			}
		}
	}

	private void OnWheel(float dy)
	{
		float num = dy;
		if (Mathf.Abs(num) < 0.9f)
		{
			num *= 10f;
		}
		_scrollY -= num * 90f;
		UpdateScrollAndCells();
	}

	private void GoPage(int page)
	{
		int num = Mathf.Max(1, _pageRows);
		int num2 = ((_view.Count != 0) ? ((_view.Count + _cols - 1) / _cols) : 0);
		int num3 = ((num2 <= 0) ? 1 : ((num2 + num - 1) / num));
		page = Mathf.Clamp(page, 0, num3 - 1);
		float num4 = (float)num * (_cellH + 12f);
		float num5 = Mathf.Max(0f, _contentH - _viewH);
		_scrollY = Mathf.Clamp((float)page * num4, 0f, num5);
		UpdateScrollAndCells();
	}

	private void OnPageFirst()
	{
		GoPage(0);
	}

	private void OnPageLast()
	{
		GoPage(_pageCount - 1);
	}

	private void OnPageSlot(int slot)
	{
		if (slot < 0 || slot >= _pageItems.Count)
		{
			return;
		}
		int num = _pageItems[slot];
		if (num == -1)
		{
			GoPage(0);
		}
		else if (num > 0)
		{
			if (num == _pageCur + 1)
			{
				OnPageJump();
			}
			else
			{
				GoPage(num - 1);
			}
		}
	}

	private void OnPageJump()
	{
		int num = Mathf.Max(1, _pageRows);
		int num2 = ((_view.Count != 0) ? ((_view.Count + _cols - 1) / _cols) : 0);
		int num3 = ((num2 <= 0) ? 1 : ((num2 + num - 1) / num));
		ShowInput("跳到第几页", (_pageCur + 1).ToString(CultureInfo.InvariantCulture), delegate(string v)
		{
			if (int.TryParse(v.Trim(), out var result))
			{
				GoPage(result - 1);
			}
		}, "1 - " + num3);
	}

	private void UpdatePager(int rowsFit, int rowCount, float rowH)
	{
		if ((Object)(object)_pageGo == (Object)null || _pageBtns.Count == 0)
		{
			return;
		}
		int num = Mathf.Max(1, rowsFit);
		int num2 = ((rowCount <= 0) ? 1 : ((rowCount + num - 1) / num));
		_pageRows = num;
		_pageCount = num2;
		float num3 = (float)num * rowH;
		int num4 = ((!(num3 <= 0f)) ? ((int)Mathf.Floor(_scrollY / num3 + 0.001f)) : 0);
		num4 = (_pageCur = Mathf.Clamp(num4, 0, num2 - 1));
		bool flag = num2 > 1;
		if (_pageGo.activeSelf != flag)
		{
			_pageGo.SetActive(flag);
		}
		if (!flag)
		{
			return;
		}
		BuildPageItems(num2, num4 + 1);
		LayoutPagerGroup();
		if ((Object)(object)_pagePrev != (Object)null)
		{
			((Selectable)_pagePrev).interactable = num4 > 0;
		}
		if ((Object)(object)_pageNext != (Object)null)
		{
			((Selectable)_pageNext).interactable = num4 < num2 - 1;
		}
		StringBuilder stringBuilder = new StringBuilder();
		for (int i = 0; i < _pageItems.Count; i++)
		{
			stringBuilder.Append(_pageItems[i]).Append(',');
		}
		stringBuilder.Append('|').Append(num4 + 1);
		string text = stringBuilder.ToString();
		if (text == _pageSig)
		{
			return;
		}
		_pageSig = text;
		for (int j = 0; j < _pageBtns.Count; j++)
		{
			bool flag2 = j < _pageItems.Count;
			GameObject gameObject = ((Component)_pageBtns[j]).gameObject;
			if (gameObject.activeSelf != flag2)
			{
				gameObject.SetActive(flag2);
			}
			if (!flag2)
			{
				continue;
			}
			int num5 = _pageItems[j];
			bool flag3 = num5 == num4 + 1;
			bool flag4 = num5 == -1;
			Text val = _pageTexts[j];
			string text2 = ((num5 > 0) ? num5.ToString(CultureInfo.InvariantCulture) : "…");
			if ((Object)(object)val != (Object)null)
			{
				if (val.text != "<color=#FFFFFF00><size=1>\u180e</size></color>" + text2)
				{
					val.text = "<color=#FFFFFF00><size=1>\u180e</size></color>" + text2;
				}
				((Graphic)val).color = (flag3 ? Color.white : (flag4 ? ColDim : ColSub));
			}
			Image component = ((Component)_pageBtns[j]).GetComponent<Image>();
			if ((Object)(object)component != (Object)null)
			{
				((Graphic)component).color = (flag3 ? ColAccent : ColField);
			}
			((Selectable)_pageBtns[j]).interactable = num5 > 0 || flag4;
		}
	}

	private void LayoutPagerGroup()
	{
		if ((Object)(object)_pageGo == (Object)null)
		{
			return;
		}
		RectTransform component = _pageGo.GetComponent<RectTransform>();
		if ((Object)(object)component == (Object)null)
		{
			return;
		}
		float num = _viewH;
		Vector2 sizeDelta = new Vector2(36f, num);
		if (component.sizeDelta != sizeDelta)
		{
			component.sizeDelta = sizeDelta;
		}
		Vector2 anchoredPosition = new Vector2(PagerX, ViewTop - num / 2f);
		if (component.anchoredPosition != anchoredPosition)
		{
			component.anchoredPosition = anchoredPosition;
		}
		float num2 = num / 2f - 16f;
		int num3 = Mathf.Clamp(_pageItems.Count, 1, 20);
		float num4 = (float)(num3 + 2) * 28f;
		float num5 = Mathf.Min(num2, num4 / 2f - 14f);
		if ((Object)(object)_pagePrev != (Object)null)
		{
			((Component)_pagePrev).GetComponent<RectTransform>().anchoredPosition = new Vector2(0f, num5);
		}
		if ((Object)(object)_pageNext != (Object)null)
		{
			((Component)_pageNext).GetComponent<RectTransform>().anchoredPosition = new Vector2(0f, num5 - (float)(num3 + 1) * 28f);
		}
		for (int i = 0; i < _pageBtns.Count; i++)
		{
			RectTransform component2 = ((Component)_pageBtns[i]).GetComponent<RectTransform>();
			if (!((Object)(object)component2 == (Object)null))
			{
				component2.anchoredPosition = new Vector2(0f, num5 - (float)(i + 1) * 28f);
			}
		}
	}

	private void BuildPageItems(int pageCount, int p)
	{
		_pageItems.Clear();
		int num = Mathf.Max(1, Mathf.Min(20, _pageBtns.Count));
		if (pageCount <= num)
		{
			for (int i = 1; i <= pageCount; i++)
			{
				_pageItems.Add(i);
			}
			return;
		}
		int num2 = Mathf.Clamp(p - num / 2, 1, pageCount - num + 1);
		for (int j = 0; j < num; j++)
		{
			_pageItems.Add(num2 + j);
		}
	}

	private void OnScrollBar(float t)
	{
		float num = Mathf.Max(0f, _contentH - _viewH);
		_scrollY = Mathf.Clamp01(t) * num;
		UpdateScrollAndCells();
	}

	private void OnDrag(float dy)
	{
		_scrollY += dy;
		UpdateScrollAndCells();
	}

	private GameObject CreateCell(int idx)
	{
		if (idx < 0 || idx >= _view.Count)
		{
			return null;
		}
		Entry entry = _view[idx];
		float pad = 6f;
		float infoH = 46f;
		float thumbW = _cellW - pad * 2f;
		float thumbH = _cellH - infoH - pad * 2f;
		RectTransform val = NewRect("Cell" + idx, (Transform)(object)_contentRect, _cellW, _cellH, 0f, 0f, ColCard, raycast: true);
		val.anchorMin = new Vector2(0f, 1f);
		val.anchorMax = new Vector2(0f, 1f);
		val.pivot = new Vector2(0f, 1f);
		UiTheme.Round(((Component)val).GetComponent<Image>(), RCard);
		RectTransform bgRt = NewRect("Bg", (Transform)(object)val, _cellW - 2f, _cellH - 2f, 0f, 0f, ColCard, raycast: false);
		UiTheme.Round(((Component)bgRt).GetComponent<Image>(), RCard - 1);
		((Transform)bgRt).SetSiblingIndex(0);
		RawImage val4 = NewRoundImage("Thumb", (Transform)(object)val, thumbW, thumbH, 0f, 0f, RItem, UiTheme.ThumbBg);
		RectTransform val3 = ((Component)((Component)val4).transform.parent).GetComponent<RectTransform>();
		val3.anchorMin = new Vector2(0f, 1f);
		val3.anchorMax = new Vector2(0f, 1f);
		val3.pivot = new Vector2(0f, 1f);
		val3.anchoredPosition = new Vector2(pad, 0f - pad);
		val4.texture = (Texture)(object)(((Object)(object)entry.Tex != (Object)null) ? entry.Tex : PlaceholderTex());
		RectTransform val5 = NewRound("SelOverlay", (Transform)(object)val, thumbW, thumbH, 0f, 0f, ColAccentSoft, RItem, false);
		val5.anchorMin = new Vector2(0f, 1f);
		val5.anchorMax = new Vector2(0f, 1f);
		val5.pivot = new Vector2(0f, 1f);
		val5.anchoredPosition = new Vector2(pad, 0f - pad);
		((Component)val5).gameObject.SetActive(false);
		Text val6 = NewLabel((Transform)(object)val, "★", 18f, 18f, 0f, 0f, 14, ColGold, (TextAnchor)4);
		((Object)val6).name = "FavStar";
		Outline val7 = ((Component)val6).gameObject.AddComponent<Outline>();
		((Shadow)val7).effectColor = new Color(0f, 0f, 0f, 0.85f);
		((Shadow)val7).effectDistance = new Vector2(1.2f, -1.2f);
		RectTransform component = ((Component)val6).GetComponent<RectTransform>();
		component.anchorMin = new Vector2(0f, 1f);
		component.anchorMax = new Vector2(0f, 1f);
		component.pivot = new Vector2(0f, 1f);
		component.anchoredPosition = new Vector2(pad + 3f, 0f - pad - 2f);
		((Component)val6).gameObject.SetActive(false);
		RectTransform val8 = NewRound("MultiMark", (Transform)(object)val, 20f, 20f, 0f, 0f, ColAccent, 9, false);
		val8.anchorMin = new Vector2(1f, 1f);
		val8.anchorMax = new Vector2(1f, 1f);
		val8.pivot = new Vector2(1f, 1f);
		val8.anchoredPosition = new Vector2(0f - pad - 2f, 0f - pad - 2f);
		RectTransform markA = NewRound("MarkA", (Transform)(object)val8, 4f, 2f, -2.95f, -0.9f, Color.white, 1, false);
		markA.localEulerAngles = new Vector3(0f, 0f, -50f);
		RectTransform markB = NewRound("MarkB", (Transform)(object)val8, 8f, 2f, 1.15f, 0.3f, Color.white, 1, false);
		markB.localEulerAngles = new Vector3(0f, 0f, 43.5f);
		((Component)val8).gameObject.SetActive(false);
		Text val10 = NewLabel((Transform)(object)val, entry.Name, _cellW - pad * 2f - 4f, 20f, 0f, 0f, 13, ColCardText, (TextAnchor)3);
		((Object)val10).name = "Name";
		RectTransform component2 = ((Component)val10).GetComponent<RectTransform>();
		component2.anchorMin = new Vector2(0f, 0f);
		component2.anchorMax = new Vector2(0f, 0f);
		component2.pivot = new Vector2(0f, 0f);
		component2.anchoredPosition = new Vector2(pad + 4f, 24f);
		val10.verticalOverflow = (VerticalWrapMode)0;
		Text val11 = NewLabel((Transform)(object)val, "", _cellW - pad * 2f - 4f, 16f, 0f, 0f, 11, ColDim, (TextAnchor)3);
		((Object)val11).name = "Sub";
		RectTransform component3 = ((Component)val11).GetComponent<RectTransform>();
		component3.anchorMin = new Vector2(0f, 0f);
		component3.anchorMax = new Vector2(0f, 0f);
		component3.pivot = new Vector2(0f, 0f);
		component3.anchoredPosition = new Vector2(pad + 4f, 8f);
		CellClick cellClick = ((Component)val).gameObject.AddComponent<CellClick>();
		cellClick.Index = idx;
		cellClick.LeftHandler = OnCellLeftClick;
		cellClick.DoubleHandler = OnCellDoubleClick;
		cellClick.RightHandler = OnCellRightClick;
		return ((Component)val).gameObject;
	}

	private void OnCellLeftClick(int idx, bool ctrl, bool shift)
	{
		if (idx < 0 || idx >= _view.Count)
		{
			return;
		}
		if ((Object)(object)_searchField != (Object)null && _searchField.isFocused)
		{
			_searchField.DeactivateInputField();
		}
		if (_batchMode)
		{
			if (shift && _anchorIdx >= 0 && _anchorIdx < _view.Count)
			{
				int num = Mathf.Min(_anchorIdx, idx);
				int num2 = Mathf.Max(_anchorIdx, idx);
				for (int i = num; i <= num2; i++)
				{
					_multi.Add(_view[i].Path);
				}
			}
			else if (!_multi.Remove(_view[idx].Path))
			{
				_multi.Add(_view[idx].Path);
			}
			_anchorIdx = idx;
			RedrawCells();
			UpdateSelectionLabel();
		}
		else if (shift && _anchorIdx >= 0 && _anchorIdx < _view.Count)
		{
			EnterBatchMode();
			int num3 = Mathf.Min(_anchorIdx, idx);
			int num4 = Mathf.Max(_anchorIdx, idx);
			for (int j = num3; j <= num4; j++)
			{
				_multi.Add(_view[j].Path);
			}
			_anchorIdx = idx;
			_selected = idx;
			RedrawCells();
			UpdateSelectionLabel();
			RefreshDetail();
		}
		else if (ctrl)
		{
			EnterBatchMode();
			if (!_multi.Remove(_view[idx].Path))
			{
				_multi.Add(_view[idx].Path);
			}
			_anchorIdx = idx;
			RedrawCells();
			UpdateSelectionLabel();
		}
		else
		{
			_multi.Clear();
			_anchorIdx = idx;
			SelectIndex(idx);
			ShowCardMenu(idx);
		}
	}

	private void OnCellDoubleClick(int idx)
	{
		if (idx >= 0 && idx < _view.Count && !_batchMode)
		{
			SelectIndex(idx);
			OnClickLoad();
		}
	}

	private void OnCellRightClick(int idx)
	{
		if (idx >= 0 && idx < _view.Count)
		{
			if (_batchMode && _multi.Contains(_view[idx].Path))
			{
				_selected = idx;
				UpdateSelectionLabel();
				RefreshDetail();
			}
			else
			{
				SelectIndex(idx);
			}
			ShowContextMenu(idx);
		}
	}

	private static void MenuAdd(List<string> L, List<Color> C, List<UnityAction> A, string label, Color col, UnityAction act)
	{
		L.Add(label);
		C.Add(col);
		A.Add(act);
	}

	private void ShowContextMenu(int idx)
	{
		CloseContextMenu();
		if (idx < 0 || idx >= _view.Count)
		{
			return;
		}
		Entry e = _view[idx];
		List<Entry> list = new List<Entry>();
		bool flag = _multi.Count > 1 && _multi.Contains(e.Path);
		if (flag)
		{
			for (int i = 0; i < _view.Count; i++)
			{
				if (_multi.Contains(_view[i].Path))
				{
					list.Add(_view[i]);
				}
			}
			if (list.Count < 2)
			{
				flag = false;
			}
		}
		if (!flag)
		{
			list.Add(e);
		}
		int count = list.Count;
		bool allFav = true;
		for (int j = 0; j < count; j++)
		{
			if (!_favs.Contains(list[j].Path))
			{
				allFav = false;
				break;
			}
		}
		Coll coll = (string.IsNullOrEmpty(_collFilter) ? null : CollByName(_collFilter));
		int num = 0;
		if (coll != null)
		{
			for (int k = 0; k < count; k++)
			{
				if (coll.Paths.Contains(list[k].Path))
				{
					num++;
				}
			}
		}
		List<string> list2 = new List<string>();
		List<Color> list3 = new List<Color>();
		List<UnityAction> list4 = new List<UnityAction>();
		Color col = default(Color);
		col = UiTheme.Text;
		Color col2 = default(Color);
		col2 = UiTheme.Accent;
		Color col3 = default(Color);
		col3 = UiTheme.Gold;
		Color col4 = default(Color);
		col4 = UiTheme.Danger;
		if (flag)
		{
			List<string> paths = new List<string>();
			for (int l = 0; l < count; l++)
			{
				paths.Add(list[l].Path);
			}
			List<Entry> tgl = list;
			string cname = coll?.Name;
			MenuAdd(list2, list3, list4, (allFav ? "取消收藏  " : "加入收藏  ") + count + " 张", col, (UnityAction)delegate
			{
				SetFavBatch(paths, !allFav);
			});
			if (coll != null && num > 0)
			{
				MenuAdd(list2, list3, list4, "移出「" + Clip(cname, 8) + "」  " + num + " 张", col3, (UnityAction)delegate
				{
					RemoveEntriesFromColl(tgl, cname);
				});
			}
			MenuAdd(list2, list3, list4, "移入其它分类…  " + count + " 张", col2, (UnityAction)delegate
			{
				_pickMoveFrom = null;
				ShowCollPicker();
			});
			MenuAdd(list2, list3, list4, "复制 " + count + " 个卡名", col, (UnityAction)delegate
			{
				StringBuilder stringBuilder2 = new StringBuilder();
				for (int num5 = 0; num5 < tgl.Count; num5++)
				{
					stringBuilder2.AppendLine(tgl[num5].Name);
				}
				CopyText(stringBuilder2.ToString());
			});
			MenuAdd(list2, list3, list4, "复制 " + count + " 条路径", col, (UnityAction)delegate
			{
				StringBuilder stringBuilder = new StringBuilder();
				for (int n = 0; n < tgl.Count; n++)
				{
					stringBuilder.AppendLine(tgl[n].Path);
				}
				CopyText(stringBuilder.ToString());
			});
			MenuAdd(list2, list3, list4, "只看这个作者", col, (UnityAction)delegate
			{
				PickAuthor(e.Author);
			});
			MenuAdd(list2, list3, list4, "删除选中的 " + count + " 张", col4, (UnityAction)delegate
			{
				OnBatchDelete();
			});
		}
		else
		{
			MenuAdd(list2, list3, list4, "只看这个作者", col, (UnityAction)delegate
			{
				PickAuthor(e.Author);
			});
			MenuAdd(list2, list3, list4, "打开所在文件夹", col, (UnityAction)delegate
			{
				RevealInExplorer(e.Path);
			});
			MenuAdd(list2, list3, list4, "复制卡名", col, (UnityAction)delegate
			{
				CopyText(e.Name);
			});
			MenuAdd(list2, list3, list4, "复制完整路径", col, (UnityAction)delegate
			{
				CopyText(e.Path);
			});
		}
		ShowMenuList(list2, list3, list4, 220f);
	}

	private void ShowCardMenu(int idx)
	{
		CloseContextMenu();
		if (idx < 0 || idx >= _view.Count)
		{
			return;
		}
		Entry e = _view[idx];
		bool flag = _favs.Contains(e.Path);
		List<string> list = new List<string>();
		List<Color> list2 = new List<Color>();
		List<UnityAction> list3 = new List<UnityAction>();
		MenuAdd(list, list2, list3, "读取", Color.white, (UnityAction)delegate
		{
			OnClickLoad();
		});
		MenuAdd(list, list2, list3, "导入", ColSub, (UnityAction)delegate
		{
			OnClickImport();
		});
		MenuAdd(list, list2, list3, "卡片详情", ColSub, (UnityAction)delegate
		{
			ShowDetailModal(idx);
		});
		MenuAdd(list, list2, list3, flag ? "取消收藏" : "加入收藏", ColGold, (UnityAction)delegate
		{
			ToggleFav(e.Path);
		});
		Coll curColl = (string.IsNullOrEmpty(_collFilter) ? null : CollByName(_collFilter));
		if (curColl != null && curColl.Paths.Contains(e.Path))
		{
			string cn = curColl.Name;
			List<Entry> one = new List<Entry>();
			one.Add(e);
			MenuAdd(list, list2, list3, "移出「" + Clip(cn, 10) + "」", ColGold, (UnityAction)delegate
			{
				RemoveEntriesFromColl(one, cn);
			});
		}
		if (!string.IsNullOrEmpty(_collFilter))
		{
			MenuAdd(list, list2, list3, "移动到其他分类", ColAccent, (UnityAction)delegate
			{
				_pickMoveFrom = _collFilter;
				ShowCollPicker();
			});
		}
		else
		{
			MenuAdd(list, list2, list3, "加入分类", ColAccent, (UnityAction)delegate
			{
				_pickMoveFrom = null;
				ShowCollPicker();
			});
		}
		MenuAdd(list, list2, list3, "删除", ColDanger, (UnityAction)delegate
		{
			OnClickDelete();
		});
		ShowMenuList(list, list2, list3, 180f);
	}

	private void ShowMenuList(List<string> items, List<Color> cols, List<UnityAction> acts, float width)
	{
		int count = items.Count;
		if (count == 0)
		{
			return;
		}
		RectTransform val = NewRound("ContextMenu", (Transform)(object)_rootRect, width, 8f + 38f * (float)count, 0f, 0f, UiTheme.SurfaceAlt, RCard, true);
		val.pivot = new Vector2(0f, 1f);
		_menuGo = ((Component)val).gameObject;
		Vector2 val2 = default(Vector2);
		val2 = new Vector2(Input.mousePosition.x, Input.mousePosition.y);
		Vector2 val3 = default(Vector2);
		if (RectTransformUtility.ScreenPointToLocalPointInRectangle(_canvasRect, val2, (Camera)null, out val3))
		{
			Rect rect = _canvasRect.rect;
			float num = rect.width / 2f - width - 10f;
			rect = _canvasRect.rect;
			float num2 = rect.height / 2f - val.sizeDelta.y - 10f;
			val3.x = Mathf.Clamp(val3.x, 0f - (rect.width / 2f - 10f), num);
			val3.y = Mathf.Clamp(val3.y, 0f - num2, rect.height / 2f - 10f);
			val.anchoredPosition = val3;
		}
		float num3 = val.sizeDelta.y / 2f - 22f;
		for (int i = 0; i < count; i++)
		{
			int num4 = i;
			MenuItem(val, items[i], num3, (UnityAction)delegate
			{
				CloseContextMenu();
				UnityAction unityAction = acts[num4];
				if (unityAction != null)
				{
					unityAction.Invoke();
				}
			}, cols[i]);
			num3 -= 38f;
		}
	}

	private void CloseDetailModal()
	{
		if ((Object)(object)_detailModalGo != (Object)null)
		{
			Object.Destroy((Object)(object)_detailModalGo);
			_detailModalGo = null;
		}
	}

	private void ShowDetailModal(int idx)
	{
		CloseDetailModal();
		CloseContextMenu();
		if (idx < 0 || idx >= _view.Count)
		{
			return;
		}
		Entry entry = _view[idx];
		GameObject val = new GameObject("DetailModalBack");
		RectTransform val2 = val.AddComponent<RectTransform>();
		((Transform)val2).SetParent((Transform)(object)_rootRect, false);
		val2.anchorMin = Vector2.zero;
		val2.anchorMax = Vector2.one;
		val2.offsetMin = Vector2.zero;
		val2.offsetMax = Vector2.zero;
		Image val3 = val.AddComponent<Image>();
		((Graphic)val3).color = new Color(0f, 0f, 0f, 0.5f);
		((Graphic)val3).raycastTarget = true;
		Button val4 = val.AddComponent<Button>();
		((Selectable)val4).transition = (Selectable.Transition)0;
		((UnityEvent)val4.onClick).AddListener(new UnityAction(CloseDetailModal));
		_detailModalGo = val;
		RectTransform val5 = NewCard("DetailCard", (Transform)(object)val2, 560f, 600f, 0f, 0f, UiTheme.Surface, RCard, true);
		NewLabel((Transform)(object)val5, "卡片详情", 200f, 24f, -259f + 100f, 272f, 16, ColText, (TextAnchor)3);
		Button val6 = NewButton((Transform)(object)val5, "×", 30f, 30f, 243f, 272f, new UnityAction(CloseDetailModal));
		PaintButton(val6, ColField, ColSub);
		NewRect("DetailSep", (Transform)(object)val5, 512f, 1f, 0f, 246f, ColLine, raycast: false);
		RawImage val7 = NewRoundImage("DThumb", (Transform)(object)val5, 512f, 288f, 0f, 94f, RItem, UiTheme.ThumbBg);
		((Graphic)val7).color = (Color)(((Object)(object)entry.Tex != (Object)null) ? Color.white : new Color(1f, 1f, 1f, 0f));
		val7.texture = (Texture)(object)(((Object)(object)entry.Tex != (Object)null) ? entry.Tex : PlaceholderTex());
		Text val8 = NewLabel((Transform)(object)val5, entry.Name, 512f, 26f, 0f, -72f, 16, ColText, (TextAnchor)3);
		((Object)val8).name = "DetailName";
		Text val9 = NewLabel((Transform)(object)val5, Path.GetFileName(entry.Path), 512f, 16f, 0f, -98f, 11, ColDim, (TextAnchor)3);
		((Object)val9).name = "DetailFile";
		string[] array = new string[8] { "作者", "对象", "大小", "时长", "创建", "修改", "版本", "类型" };
		string[] array2 = new string[8]
		{
			string.IsNullOrEmpty(entry.Author) ? "未分类" : entry.Author,
			(entry.ObjCount > 0) ? (entry.ObjCount + " 个") : ((entry.VerParsed == 0) ? "解析中…" : "未知"),
			FormatSize(entry.Size),
			(entry.DurParsed == 0) ? "解析中…" : ((entry.Dur > 0f) ? FormatDur(entry.Dur) : "—"),
			(entry.CTime == DateTime.MinValue) ? "未知" : entry.CTime.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture),
			(entry.MTime == DateTime.MinValue) ? "未知" : entry.MTime.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture),
			string.IsNullOrEmpty(entry.Ver) ? "未知" : entry.Ver,
			string.IsNullOrEmpty(entry.Ver) ? "未知" : (entry.Ver.StartsWith("1.1") ? "KKS 场景卡" : "KK 场景卡")
		};
		float num = -132f;
		for (int i = 0; i < array.Length; i++)
		{
			bool right = i >= 4;
			float lx = (right ? 37f : (-231f));
			float vx = (right ? 163f : (-105f));
			float ry = num - (float)(i % 4) * 28f;
			Text val10 = NewLabel((Transform)(object)val5, array[i], 56f, 22f, lx, ry, 12, ColDim, (TextAnchor)3);
			((Object)val10).name = "DLab" + i;
			Text val11 = NewLabel((Transform)(object)val5, array2[i], 180f, 22f, vx, ry, 12, ColSub, (TextAnchor)3);
			((Object)val11).name = "DVal" + i;
		}
		Text val12 = NewLabel((Transform)(object)val5, entry.Path, 512f, 44f, 0f, -252f, 10, ColDim, (TextAnchor)6);
		val12.alignment = (TextAnchor)0;
		val12.horizontalOverflow = (HorizontalWrapMode)0;
		val12.verticalOverflow = (VerticalWrapMode)0;
	}

	private void RemoveEntriesFromColl(List<Entry> tg, string collName)
	{
		Coll coll = CollByName(collName);
		if (coll == null || tg == null || tg.Count == 0)
		{
			return;
		}
		int num = 0;
		for (int i = 0; i < tg.Count; i++)
		{
			if (coll.Paths.Remove(tg[i].Path))
			{
				num++;
			}
			_multi.Remove(tg[i].Path);
		}
		SaveColls();
		Log.LogInfo((object)("collection " + coll.Name + ": removed " + num + " (ctx menu)"));
		UpdateToggleVisuals();
		UpdateSelectionLabel();
		RefreshView(keepPos: true);
		if ((Object)(object)_pickGo != (Object)null)
		{
			ShowCollPicker();
		}
	}

	private void SetFavBatch(List<string> paths, bool add)
	{
		if (paths == null || paths.Count == 0)
		{
			return;
		}
		for (int i = 0; i < paths.Count; i++)
		{
			if (add)
			{
				_favs.Add(paths[i]);
			}
			else
			{
				_favs.Remove(paths[i]);
			}
		}
		SaveFavs();
		UpdateSelectionLabel();
		RefreshView(keepPos: true);
		Log.LogInfo((object)("batch fav(" + (add ? "add" : "remove") + "): " + paths.Count));
	}

	private void MenuItem(RectTransform parent, string txt, float y, UnityAction onClick)
	{
		MenuItem(parent, txt, y, onClick, UiTheme.Text);
	}

	private void MenuItem(RectTransform parent, string txt, float y, UnityAction onClick, Color col)
	{
		float num = parent.sizeDelta.x - 12f;
		Button val = NewButton((Transform)(object)parent, txt, num, 34f, 0f, y, onClick);
		Image component = ((Component)val).GetComponent<Image>();
		if ((Object)(object)component != (Object)null)
		{
			((Graphic)component).color = ColField;
		}
		Text component2 = ((Component)((Component)val).transform.Find("Label")).GetComponent<Text>();
		if ((Object)(object)component2 != (Object)null)
		{
			component2.alignment = (TextAnchor)3;
			((Graphic)component2).color = col;
			RectTransform component3 = ((Component)component2).GetComponent<RectTransform>();
			component3.sizeDelta = new Vector2(num - 16f, 34f);
			component3.anchoredPosition = new Vector2(0f, 0f);
		}
	}

	private void CloseContextMenu()
	{
		if ((Object)(object)_menuGo != (Object)null)
		{
			Object.Destroy((Object)(object)_menuGo);
			_menuGo = null;
		}
	}

	private static string NodeDir(string root, string author, string path)
	{
		if (string.IsNullOrEmpty(root))
		{
			return null;
		}
		string text = root;
		if (!string.IsNullOrEmpty(author))
		{
			text = Path.Combine(text, author);
		}
		if (!string.IsNullOrEmpty(path))
		{
			text = Path.Combine(text, path.Replace('/', '\\'));
		}
		return text;
	}

	private void OnSideRowClick(SideRow row)
	{
		if (row == null)
		{
			return;
		}
		float now = Time.realtimeSinceStartup;
		bool isDouble = !string.IsNullOrEmpty(row.Dir) && _sideClickKey == row.Key && now - _sideClickTime <= 0.4f;
		_sideClickKey = row.Key;
		_sideClickTime = now;
		if (isDouble)
		{
			_sideClickKey = null;
			_sideClickTime = -10f;
			Log.LogInfo((object)("side dblclick: " + row.Dir));
			OpenDirInExplorer(row.Dir);
			return;
		}
		if (row.OnClick != null)
		{
			row.OnClick.Invoke();
		}
	}

	private void OpenDirInExplorer(string dir)
	{
		try
		{
			if (!string.IsNullOrEmpty(dir) && Directory.Exists(dir))
			{
				Process.Start("explorer.exe", "\"" + dir + "\"");
			}
		}
		catch (Exception ex)
		{
			Log.LogError((object)("reveal dir: " + ex));
		}
	}

	private void RevealInExplorer(string path)
	{
		try
		{
			string directoryName = Path.GetDirectoryName(path);
			if (!string.IsNullOrEmpty(directoryName) && Directory.Exists(directoryName))
			{
				Process.Start("explorer.exe", "/select,\"" + path + "\"");
			}
		}
		catch (Exception ex)
		{
			Log.LogError((object)("reveal: " + ex));
		}
	}

	private void CopyText(string s)
	{
		try
		{
			GUIUtility.systemCopyBuffer = s ?? "";
		}
		catch (Exception ex)
		{
			Log.LogError((object)("copy: " + ex));
		}
	}

	private void RedrawCells()
	{
		List<int> list = new List<int>(_cells.Keys);
		for (int i = 0; i < list.Count; i++)
		{
			LayoutCell(list[i]);
		}
	}

	private void SelectIndex(int idx)
	{
		if (idx >= 0 && idx < _view.Count)
		{
			if (_multi.Count > 0)
			{
				_multi.Clear();
			}
			int selected = _selected;
			_selected = idx;
			if (selected >= 0 && selected != idx)
			{
				LayoutCell(selected);
			}
			LayoutCell(idx);
			UpdateSelectionLabel();
			RefreshDetail();
		}
	}

	private void UpdateSelectionLabel()
	{
		if ((Object)(object)_selLabel == (Object)null)
		{
			return;
		}
		bool batchMode = _batchMode;
		if (batchMode)
		{
			_selLabel.text = ((_multi.Count == 0) ? "<color=#FFFFFF00><size=1>\u180e</size></color>批量模式：点卡片勾选，Shift 连选，Esc 退出" : ("<color=#FFFFFF00><size=1>\u180e</size></color>已选 " + _multi.Count + " 张"));
			return;
		}
		if (_selected < 0 || _selected >= _view.Count)
		{
			_selLabel.text = "<color=#FFFFFF00><size=1>\u180e</size></color>未选择";
			return;
		}
		Entry entry = _view[_selected];
		StringBuilder stringBuilder = new StringBuilder();
		stringBuilder.Append(entry.Name);
		stringBuilder.Append("    ·    ").Append(FormatSize(entry.Size));
		if (entry.ObjCount > 0)
		{
			stringBuilder.Append("    ·    ").Append(entry.ObjCount).Append(" 对象");
		}
		if (!string.IsNullOrEmpty(entry.Ver))
		{
			stringBuilder.Append("    ·    ").Append(entry.Ver);
		}
		if (entry.MTime != DateTime.MinValue)
		{
			stringBuilder.Append("    ·    ").Append(entry.MTime.ToString("yyyy-MM-dd HH:mm"));
		}
		_selLabel.text = "<color=#FFFFFF00><size=1>\u180e</size></color>" + stringBuilder.ToString();
	}

	private void PushNativeList()
	{
		if (!((Object)(object)_panel == (Object)null) && !(_fiListPath == null))
		{
			List<string> list = new List<string>(_view.Count);
			for (int i = 0; i < _view.Count; i++)
			{
				list.Add(_view[i].Path);
			}
			_fiListPath.SetValue(_panel, list);
			if (_fiThumbnailNum != null)
			{
				_fiThumbnailNum.SetValue(_panel, _view.Count);
			}
		}
	}

	private void ShowLoading(bool show)
	{
		if (show)
		{
			if ((Object)(object)_loadGo == (Object)null)
			{
				BuildLoadingOverlay();
			}
			_loadStart = Time.realtimeSinceStartup;
			_loading = true;
			if ((Object)(object)_loadGo != (Object)null)
			{
				_loadGo.SetActive(true);
			}
		}
		else
		{
			_loading = false;
			if ((Object)(object)_loadGo != (Object)null)
			{
				_loadGo.SetActive(false);
			}
		}
	}

	private void BuildLoadingOverlay()
	{
		if ((Object)(object)_rootRect == (Object)null)
		{
			return;
		}
		RectTransform val = NewRect("Loading", (Transform)(object)_rootRect, 10f, 10f, 0f, 0f, new Color(0f, 0f, 0f, 0.55f), raycast: true);
		val.anchorMin = Vector2.zero;
		val.anchorMax = Vector2.one;
		val.offsetMin = Vector2.zero;
		val.offsetMax = Vector2.zero;
		_loadGo = ((Component)val).gameObject;
		RectTransform card = NewCard("LoadCard", (Transform)(object)val, 300f, 96f, 0f, 0f, UiTheme.SurfaceAlt, RCard, true);
		_loadText = NewLabel((Transform)(object)card, "正在读取场景卡", 200f, 22f, 26f, 14f, 14, ColText, (TextAnchor)4);
		NewLabel((Transform)(object)card, "读取完成后窗口自动关闭", 200f, 18f, 26f, -14f, 11, ColSub, (TextAnchor)4);
		_loadSpin = new Image[8];
		for (int i = 0; i < 8; i++)
		{
			float f = (float)i * 45f * ((float)Math.PI / 180f);
			RectTransform val2 = NewRound("Spin" + i, (Transform)(object)card, 5f, 5f, -100f + Mathf.Cos(f) * 14f, Mathf.Sin(f) * 14f, ColAccent, 2, false);
			_loadSpin[i] = ((Component)val2).GetComponent<Image>();
		}
	}

	private void AnimateLoading()
	{
		float num = Time.realtimeSinceStartup - _loadStart;
		if ((Object)(object)_loadText != (Object)null)
		{
			int num2 = (int)(num * 3f) % 4;
			string text = "<color=#FFFFFF00><size=1>\u180e</size></color>正在读取场景卡" + new string('.', num2);
			if (_loadText.text != text)
			{
				_loadText.text = text;
			}
		}
		if (_loadSpin == null)
		{
			return;
		}
		int num3 = (int)(num * 9f) % 8;
		for (int i = 0; i < _loadSpin.Length; i++)
		{
			if ((Object)(object)_loadSpin[i] == (Object)null)
			{
				continue;
			}
			int num4 = ((num3 - i) % 8 + 8) % 8;
			float a = 0.15f + 0.85f * (1f - (float)num4 / 8f);
			((Graphic)_loadSpin[i]).color = new Color(ColAccent.r, ColAccent.g, ColAccent.b, a);
		}
	}

	private void OnClickLoad()
	{
		if (_selected < 0 || _selected >= _view.Count)
		{
			return;
		}
		try
		{
			PushNativeList();
			if (_fiSelect != null)
			{
				_fiSelect.SetValue(_panel, _selected);
			}
			if (_miOnClickLoad != null)
			{
				ShowLoading(true);
				_miOnClickLoad.Invoke(_panel, null);
			}
			AddRecent(_view[_selected].Path);
			_lastLoadedDir = Path.GetDirectoryName(_view[_selected].Path);
			_lastLoadedAuthor = _author;
			_lastLoadedSub = _sub;
			Log.LogInfo((object)("load: " + _view[_selected].Path));
		}
		catch (Exception ex)
		{
			Log.LogError((object)("load: " + ex));
		}
	}

	private void OnClickImport()
	{
		if (_selected < 0 || _selected >= _view.Count)
		{
			return;
		}
		try
		{
			PushNativeList();
			if (_fiSelect != null)
			{
				_fiSelect.SetValue(_panel, _selected);
			}
			if (_miOnClickImport != null)
			{
				_miOnClickImport.Invoke(_panel, null);
			}
			_lastLoadedDir = Path.GetDirectoryName(_view[_selected].Path);
			_lastLoadedAuthor = _author;
			_lastLoadedSub = _sub;
			Log.LogInfo((object)("import: " + _view[_selected].Path));
		}
		catch (Exception ex)
		{
			Log.LogError((object)("import: " + ex));
		}
	}

	private string CollFile()
	{
		try
		{
			return Path.Combine(Paths.ConfigPath, "kk.scene.browser.collections.txt");
		}
		catch (Exception)
		{
			return null;
		}
	}

	private void LoadColls()
	{
		try
		{
			string text = CollFile();
			if (text == null || !File.Exists(text))
			{
				return;
			}
			string[] array = File.ReadAllLines(text);
			Coll coll = null;
			for (int i = 0; i < array.Length; i++)
			{
				string text2 = array[i].Trim();
				if (text2.Length == 0)
				{
					continue;
				}
				if (text2[0] == '#')
				{
					string text3 = text2.Substring(1).Trim();
					if (text3.Length == 0)
					{
						coll = null;
						continue;
					}
					coll = CollByName(text3);
					if (coll == null)
					{
						coll = new Coll();
						coll.Name = text3;
						_colls.Add(coll);
					}
				}
				else
				{
					coll?.Paths.Add(text2);
				}
			}
			Log.LogInfo((object)("collections loaded: " + _colls.Count));
		}
		catch (Exception ex)
		{
			Log.LogError((object)("colls load: " + ex));
		}
	}

	private void SaveColls()
	{
		try
		{
			string text = CollFile();
			if (text == null)
			{
				return;
			}
			List<string> list = new List<string>();
			for (int i = 0; i < _colls.Count; i++)
			{
				list.Add("#" + _colls[i].Name);
				string[] array = new string[_colls[i].Paths.Count];
				_colls[i].Paths.CopyTo(array);
				Array.Sort(array, StringComparer.OrdinalIgnoreCase);
				for (int j = 0; j < array.Length; j++)
				{
					list.Add(array[j]);
				}
			}
			File.WriteAllLines(text, list.ToArray());
		}
		catch (Exception ex)
		{
			Log.LogError((object)("colls save: " + ex));
		}
	}

	private Coll CollByName(string name)
	{
		if (string.IsNullOrEmpty(name))
		{
			return null;
		}
		for (int i = 0; i < _colls.Count; i++)
		{
			if (string.Equals(_colls[i].Name, name, StringComparison.OrdinalIgnoreCase))
			{
				return _colls[i];
			}
		}
		return null;
	}

	private int CollFilterIndex()
	{
		if (string.IsNullOrEmpty(_collFilter))
		{
			return 0;
		}
		for (int i = 0; i < _colls.Count; i++)
		{
			if (string.Equals(_colls[i].Name, _collFilter, StringComparison.OrdinalIgnoreCase))
			{
				return i + 2;
			}
		}
		return 0;
	}

	private void AddCollection(string name)
	{
		name = (name ?? "").Trim();
		if (name.Length == 0)
		{
			return;
		}
		if (name[0] == '#')
		{
			name = name.Substring(1).Trim();
		}
		if (name.Length == 0)
		{
			return;
		}
		if (CollByName(name) != null)
		{
			_collFilter = name;
			UpdateToggleVisuals();
			RefreshView();
			return;
		}
		Coll coll = new Coll();
		coll.Name = name;
		_colls.Add(coll);
		SaveColls();
		_collFilter = name;
		if ((Object)(object)_pickGo != (Object)null)
		{
			ShowCollPicker();
		}
		UpdateToggleVisuals();
		RefreshView();
		Log.LogInfo((object)("collection added: " + name));
	}

	private void RenameCollection(string oldName, string newName)
	{
		Coll coll = CollByName(oldName);
		newName = (newName ?? "").Trim();
		if (newName.Length > 0 && newName[0] == '#')
		{
			newName = newName.Substring(1).Trim();
		}
		if (coll == null || newName.Length == 0)
		{
			return;
		}
		Coll coll2 = CollByName(newName);
		if (coll2 == null || coll2 == coll)
		{
			if (string.Equals(_collFilter, coll.Name, StringComparison.OrdinalIgnoreCase))
			{
				_collFilter = newName;
			}
			coll.Name = newName;
			SaveColls();
			if ((Object)(object)_pickGo != (Object)null)
			{
				ShowCollPicker();
			}
			UpdateToggleVisuals();
			RefreshView();
		}
	}

	private void DeleteCollection(string name)
	{
		Coll coll = CollByName(name);
		if (coll != null)
		{
			_colls.Remove(coll);
			if (string.Equals(_collFilter, name, StringComparison.OrdinalIgnoreCase))
			{
				_collFilter = null;
			}
			SaveColls();
			if ((Object)(object)_pickGo != (Object)null)
			{
				ShowCollPicker();
			}
			UpdateToggleVisuals();
			RefreshView();
			Log.LogInfo((object)("collection removed: " + name));
		}
	}

	private List<Entry> SelEntries()
	{
		List<Entry> list = new List<Entry>();
		if (_multi.Count > 0)
		{
			for (int i = 0; i < _view.Count; i++)
			{
				if (_multi.Contains(_view[i].Path))
				{
					list.Add(_view[i]);
				}
			}
			return list;
		}
		if (_selected >= 0 && _selected < _view.Count)
		{
			list.Add(_view[_selected]);
		}
		return list;
	}

	private void ApplyToColl(string collName, bool add)
	{
		Coll coll = CollByName(collName);
		if (coll == null)
		{
			return;
		}
		List<Entry> list = SelEntries();
		if (list.Count == 0)
		{
			return;
		}
		int num = 0;
		for (int i = 0; i < list.Count; i++)
		{
			if (add)
			{
				if (coll.Paths.Add(list[i].Path))
				{
					num++;
				}
			}
			else if (coll.Paths.Remove(list[i].Path))
			{
				num++;
			}
		}
		SaveColls();
		Log.LogInfo((object)("collection " + coll.Name + ": " + (add ? "added " : "removed ") + num + " / sel " + list.Count));
		UpdateToggleVisuals();
		RefreshView(keepPos: true);
		if ((Object)(object)_pickGo != (Object)null)
		{
			ShowCollPicker();
		}
	}

	private void MoveToColl(string from, string to)
	{
		Coll src = CollByName(from);
		Coll dst = CollByName(to);
		if (src == null || dst == null || src == dst)
		{
			return;
		}
		List<Entry> list = SelEntries();
		if (list.Count == 0)
		{
			return;
		}
		int num = 0;
		for (int i = 0; i < list.Count; i++)
		{
			string path = list[i].Path;
			bool added = dst.Paths.Add(path);
			bool removed = src.Paths.Remove(path);
			if (added || removed)
			{
				num++;
			}
		}
		SaveColls();
		Log.LogInfo((object)("collection move " + src.Name + " -> " + dst.Name + ": " + num + " / sel " + list.Count));
		UpdateToggleVisuals();
		RefreshView(keepPos: true);
		if ((Object)(object)_pickGo != (Object)null)
		{
			ShowCollPicker();
		}
	}

	private void ShowCollPicker()
	{
		bool wasOpen = (Object)(object)_pickGo != (Object)null;
		CloseCollPicker();
		List<Entry> list = SelEntries();
		if (list.Count == 0)
		{
			return;
		}
		if (!wasOpen)
		{
			_pickTarget = null;
		}
		int count = _colls.Count;
		float num = 36f;
		float num2 = 658f;
		float numTop = 88f;
		float numPad = 16f;
		if (count > 0 && numTop + numPad + (float)count * num > num2)
		{
			num = Mathf.Max(22f, (num2 - numTop - numPad) / (float)count);
		}
		float num3 = numTop + numPad + (float)count * num;
		RectTransform val = NewRound("CollPicker", (Transform)(object)_rootRect, 460f, num3, 0f, 0f, UiTheme.SurfaceAlt, RCard, true);
		_pickGo = ((Component)val).gameObject;
		_pickFrame = Time.frameCount;
		string txt = ((list.Count > 1) ? ("给选中的 " + list.Count + " 张卡指定收藏夹") : "给这张卡指定收藏夹");
		Text val2 = NewLabel((Transform)(object)val, txt, 432f, 24f, 0f, num3 / 2f - 12f - 12f, 14, ColText, (TextAnchor)3);
		if ((Object)(object)val2 != (Object)null)
		{
			((Object)val2).name = "PickHead";
		}
		float num4 = num3 / 2f - numTop - num / 2f;
		for (int i = 0; i < _colls.Count; i++)
		{
			Coll coll = _colls[i];
			int num5 = 0;
			for (int j = 0; j < list.Count; j++)
			{
				if (coll.Paths.Contains(list[j].Path))
				{
					num5++;
				}
			}
			bool flag = string.Equals(_pickTarget ?? "", coll.Name, StringComparison.OrdinalIgnoreCase);
			string txt2 = (flag ? "● " : "○ ") + Clip(coll.Name, 13) + "     共 " + coll.Paths.Count + "  ·  已选 " + num5 + "/" + list.Count;
			string cn = coll.Name;
			Button val3 = NewButton((Transform)(object)val, txt2, 300f, num - 4f, -62f, num4, (UnityAction)delegate
			{
				_pickTarget = cn;
				ShowCollPicker();
			});
			((Object)val3).name = "Coll_" + i;
			Text component = ((Component)((Component)val3).transform.Find("Label")).GetComponent<Text>();
			if ((Object)(object)component != (Object)null)
			{
				component.alignment = (TextAnchor)3;
				component.fontSize = 12;
				RectTransform component2 = ((Component)component).GetComponent<RectTransform>();
				if ((Object)(object)component2 != (Object)null)
				{
					component2.sizeDelta = new Vector2(286f, num - 4f);
					component2.anchoredPosition = Vector2.zero;
				}
				((Graphic)component).color = (Color)(flag ? Color.white : UiTheme.Text);
			}
			Image component3 = ((Component)val3).GetComponent<Image>();
			if ((Object)(object)component3 != (Object)null)
			{
				((Graphic)component3).color = (Color)(flag ? ColAccent : ColField);
			}
			Button val4 = NewButton((Transform)(object)val, "改", 40f, num - 6f, 132f, num4, (UnityAction)delegate
			{
				ShowInput("重命名分类", cn, delegate(string v)
				{
					RenameCollection(cn, v);
				});
			});
			((Object)val4).name = "CollRename_" + i;
			Button val5 = NewButton((Transform)(object)val, "删", 40f, num - 6f, 178f, num4, (UnityAction)delegate
			{
				DeleteCollection(cn);
			});
			((Object)val5).name = "CollDel_" + i;
			Image component4 = ((Component)val5).GetComponent<Image>();
			if ((Object)(object)component4 != (Object)null)
			{
				((Graphic)component4).color = new Color(0.42f, 0.22f, 0.24f, 1f);
			}
			num4 -= num;
		}
		float y = num3 / 2f - 61f;
		bool has = !string.IsNullOrEmpty(_pickTarget) && CollByName(_pickTarget) != null;
		string txtAdd = (string.IsNullOrEmpty(_pickMoveFrom) ? "加入" : "移动");
		Button val6 = NewButton((Transform)(object)val, txtAdd, 88f, 34f, -148f, y, (UnityAction)delegate
		{
			if (has)
			{
				if (!string.IsNullOrEmpty(_pickMoveFrom) && !string.Equals(_pickMoveFrom, _pickTarget, StringComparison.OrdinalIgnoreCase))
				{
					MoveToColl(_pickMoveFrom, _pickTarget);
				}
				else
				{
					ApplyToColl(_pickTarget, add: true);
				}
			}
		});
		((Object)val6).name = "CollAdd";
		PaintButton(val6, has ? ColAccent : ColField, has ? Color.white : ColSub);
		Button val7 = NewButton((Transform)(object)val, "移除", 88f, 34f, -52f, y, (UnityAction)delegate
		{
			if (has)
			{
				ApplyToColl(_pickTarget, add: false);
			}
		});
		((Object)val7).name = "CollRemove";
		PaintButton(val7, has ? ColAccent : ColField, has ? Color.white : ColSub);
		NewButton((Transform)(object)val, "新建分类", 112f, 34f, 56f, y, (UnityAction)delegate
		{
			ShowInput("新建分类", "", delegate(string v)
			{
				AddCollection(v);
			});
		});
		NewButton((Transform)(object)val, "关闭", 72f, 34f, 156f, y, new UnityAction(CloseCollPicker));
	}

	private void CloseCollPicker()
	{
		if ((Object)(object)_pickGo != (Object)null)
		{
			Object.Destroy((Object)(object)_pickGo);
			_pickGo = null;
		}
	}

	private void ShowInput(string title, string initial, UnityAction<string> onOk)
	{
		ShowInput(title, initial, onOk, "分类名…");
	}

	private void ShowInput(string title, string initial, UnityAction<string> onOk, string placeholder)
	{
		CloseInput();
		_inputDone = false;
		RectTransform val = NewRound("InputBox", (Transform)(object)_rootRect, 440f, 190f, 0f, 0f, UiTheme.SurfaceAlt, RCard, true);
		_inputGo = ((Component)val).gameObject;
		NewLabel((Transform)(object)val, title, 412f, 24f, 0f, 72f, 14, ColText, (TextAnchor)3);
		RectTransform val2 = NewRound("InputArea", (Transform)(object)val, 380f, 34f, 0f, 14f, ColLine, RItem, true);
		Text val3 = NewLabel((Transform)(object)val2, "", 364f, 32f, 0f, 0f, 14, ColText, (TextAnchor)3);
		((Object)val3).name = "InputText";
		val3.supportRichText = false;
		val3.horizontalOverflow = (HorizontalWrapMode)1;
		val3.verticalOverflow = (VerticalWrapMode)0;
		Text val4 = NewLabel((Transform)(object)val2, placeholder, 364f, 32f, 0f, 0f, 14, ColDim, (TextAnchor)3);
		((Object)val4).name = "InputPlaceholder";
		_inputField = ((Component)val2).gameObject.AddComponent<InputField>();
		_inputField.textComponent = val3;
		_inputField.placeholder = (Graphic)(object)val4;
		((Selectable)_inputField).targetGraphic = (Graphic)(object)((Component)val2).GetComponent<Image>();
		_inputField.lineType = InputField.LineType.SingleLine;
		_inputField.characterLimit = 32;
		_inputField.text = initial ?? "";
		((UnityEvent<string>)(object)_inputField.onEndEdit).AddListener((UnityAction<string>)CommitInput);
		NewButton((Transform)(object)val, "取消", 96f, 36f, -54f, -65f, (UnityAction)delegate
		{
			CloseInput();
		});
		_inputCb = onOk;
		Button val5 = NewButton((Transform)(object)val, "确定", 96f, 36f, 54f, -65f, new UnityAction(CommitInputNow));
		PaintButton(val5, ColAccent, Color.white);
		((Selectable)_inputField).Select();
		_inputField.ActivateInputField();
	}

	private void CommitInput(string v)
	{
		if (!_inputDone)
		{
			_inputDone = true;
			UnityAction<string> inputCb = _inputCb;
			CloseInput();
			inputCb?.Invoke(v);
			_inputCb = null;
		}
	}

	private void CommitInputNow()
	{
		string v = (((Object)(object)_inputField != (Object)null) ? _inputField.text : "");
		CommitInput(v);
	}

	private void CloseInput()
	{
		_inputDone = true;
		if ((Object)(object)_inputField != (Object)null)
		{
			try
			{
				_inputField.DeactivateInputField();
			}
			catch (Exception)
			{
			}
			_inputField = null;
		}
		if ((Object)(object)_inputGo != (Object)null)
		{
			Object.Destroy((Object)(object)_inputGo);
			_inputGo = null;
		}
	}

	private Button AddBatchButton(Transform parent, string txt, float w, ref float x, float y, UnityAction onClick)
	{
		Button val = NewButton(parent, txt, w, 32f, x + w / 2f, y, onClick);
		x += w + 8f;
		_batchGo.Add(((Component)val).gameObject);
		return val;
	}

	private void EnterBatchMode()
	{
		_batchMode = true;
		UpdateSelectionLabel();
	}

	private void ExitBatchMode()
	{
		_batchMode = false;
		_multi.Clear();
		RedrawCells();
		UpdateSelectionLabel();
	}

	private void OnClearMulti()
	{
		ExitBatchMode();
	}

	private void OnBatchUnfav()
	{
		if (_multi.Count != 0)
		{
			List<string> list = new List<string>(_multi);
			for (int i = 0; i < list.Count; i++)
			{
				_favs.Remove(list[i]);
			}
			SaveFavs();
			_multi.Clear();
			RefreshView(keepPos: true);
			Log.LogInfo((object)("batch unfav: " + list.Count));
		}
	}

	private void OnBatchFav()
	{
		if (_multi.Count != 0)
		{
			List<string> list = new List<string>(_multi);
			for (int i = 0; i < list.Count; i++)
			{
				_favs.Add(list[i]);
			}
			SaveFavs();
			_multi.Clear();
			RefreshView(keepPos: true);
			Log.LogInfo((object)("batch fav: " + list.Count));
		}
	}

	private void OnBatchDelete()
	{
		if (_multi.Count == 0)
		{
			return;
		}
		List<Entry> victims = new List<Entry>();
		lock (_all)
		{
			for (int i = 0; i < _all.Count; i++)
			{
				if (_multi.Contains(_all[i].Path))
				{
					victims.Add(_all[i]);
				}
			}
		}
		if (victims.Count == 0)
		{
			_multi.Clear();
			UpdateSelectionLabel();
		}
		else
		{
			ShowConfirm("确定删除选中的 " + victims.Count + " 张卡？\n（移入回收站，可从回收站恢复）", (UnityAction)delegate
			{
				DoBatchDelete(victims);
			});
		}
	}

	private void DoBatchDelete(List<Entry> victims)
	{
		int num = 0;
		for (int i = 0; i < victims.Count; i++)
		{
			Entry entry = victims[i];
			if (entry == null)
			{
				continue;
			}
			bool flag = MoveToRecycleBin(entry.Path);
			if (!flag)
			{
				try
				{
					File.Delete(entry.Path);
					flag = true;
				}
				catch (Exception ex)
				{
					Log.LogError((object)("delete: " + ex));
				}
			}
			if (flag)
			{
				num++;
				lock (_all)
				{
					_all.Remove(entry);
				}
				_favs.Remove(entry.Path);
				_multi.Remove(entry.Path);
				_texLru.Remove(entry);
				if ((Object)(object)entry.Tex != (Object)null)
				{
					Object.Destroy((Object)(object)entry.Tex);
					entry.Tex = null;
				}
			}
		}
		Log.LogInfo((object)("batch delete: " + num + " / " + victims.Count));
		_multi.Clear();
		RefreshView(keepPos: true);
		_sideDirty = true;
	}

	private void OnClickDelete()
	{
		if (_selected >= 0 && _selected < _view.Count)
		{
			Entry e = _view[_selected];
			ShowConfirm("确定删除这张卡？\n" + e.Name + "\n（移入回收站，可从回收站恢复）", (UnityAction)delegate
			{
				DoDelete(e);
			});
		}
	}

	private void DoDelete(Entry e)
	{
		if (e == null)
		{
			return;
		}
		bool flag = MoveToRecycleBin(e.Path);
		if (!flag)
		{
			try
			{
				File.Delete(e.Path);
				flag = true;
			}
			catch (Exception ex)
			{
				Log.LogError((object)("delete: " + ex));
			}
		}
		Log.LogInfo((object)((flag ? "deleted(trash): " : "delete FAILED: ") + e.Path));
		if (flag)
		{
			lock (_all)
			{
				_all.Remove(e);
			}
			_favs.Remove(e.Path);
			_multi.Remove(e.Path);
			_texLru.Remove(e);
			if ((Object)(object)e.Tex != (Object)null)
			{
				Object.Destroy((Object)(object)e.Tex);
				e.Tex = null;
			}
			RefreshView(keepPos: true);
			_sideDirty = true;
		}
	}

	private void OnClickClose()
	{
		try
		{
			CloseDetailModal();
			CloseContextMenu();
			CloseDrop();
			CloseSearch(clear: false);
			if ((Object)(object)_panel != (Object)null && _miOnClickClose != null)
			{
				_miOnClickClose.Invoke(_panel, null);
			}
			else
			{
				Log.LogWarning((object)("close fallback: native OnClickClose unavailable"));
				HideRoot();
				try
				{
					UnityEngine.SceneManagement.Scene val = UnityEngine.SceneManagement.SceneManager.GetSceneByName("StudioSceneLoad");
					if (val.IsValid() && val.isLoaded)
					{
						UnityEngine.SceneManagement.SceneManager.UnloadScene("StudioSceneLoad");
					}
				}
				catch (Exception ex2)
				{
					Log.LogWarning((object)("close unload: " + ex2.Message));
				}
			}
		}
		catch (Exception ex)
		{
			Log.LogError((object)("close: " + ex));
		}
	}

	private void ShowConfirm(string msg, UnityAction yes)
	{
		if ((Object)(object)_confirmGo != (Object)null)
		{
			Object.Destroy((Object)(object)_confirmGo);
			_confirmGo = null;
		}
		RectTransform val = NewRound("Confirm", (Transform)(object)_rootRect, 460f, 240f, 0f, 0f, UiTheme.SurfaceAlt, RCard, true);
		_confirmGo = ((Component)val).gameObject;
		NewLabel((Transform)(object)val, msg, 420f, 120f, 0f, 34f, 16, ColText, (TextAnchor)4);
		NewButton((Transform)(object)val, "取消", 100f, 44f, -60f, -72f, (UnityAction)delegate
		{
			Object.Destroy((Object)(object)_confirmGo);
			_confirmGo = null;
		});
		Button val2 = NewButton((Transform)(object)val, "删除", 100f, 44f, 60f, -72f, (UnityAction)delegate
		{
			Object.Destroy((Object)(object)_confirmGo);
			_confirmGo = null;
			yes.Invoke();
		});
		PaintButton(val2, ColDanger, Color.white);
	}
}


















