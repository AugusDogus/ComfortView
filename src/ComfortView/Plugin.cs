using System;
using System.Collections.Generic;
using System.Linq;
using BepInEx;
using BepInEx.Configuration;
using TMPro;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.UI;

namespace ComfortView;

[BepInPlugin("mishka.valheim.comfortview", "ComfortView Reforged", PluginVersion)]
public class ComfortViewPlugin : BaseUnityPlugin
{
	private enum DisplayMode
	{
		All,
		ActiveOnly,
		Picker
	}

	private enum MenuTab
	{
		Items,
		Nearby
	}

	private class RingEntry
	{
		public GameObject Root;

		public LineRenderer Ring;

		public LineRenderer Post;

		public LineRenderer Spoke;

		public GameObject SphereRoot;

		public LineRenderer[] SphereRings;

		public Color Color;
	}

	public const string PluginGUID = "mishka.valheim.comfortview";

	public const string PluginName = "ComfortView Reforged";

	public const string PluginVersion = "1.0.0";

	private const float ComfortRadius = 10f;

	private const float ScanRadius = 20f;

	private const float RefreshInterval = 1f;

	private const int Segments = 48;

	private const float HeightOffset = 0.05f;

	private const float DimWidth = 0.04f;

	private const float HoverWidth = 0.12f;

	private const float DimAlpha = 0.35f;

	private const float PostHeight = 2.5f;

	private const float HoverRayDistance = 50f;

	private const float SoloAimTolerance = 1.2f;

	private const float SpokeAlpha = 0.6f;

	private const float SpokeWidth = 0.05f;

	private const float SpokeDashesPerRadius = 10f;

	private const float SphereFillAlpha = 0.12f;

	private static Material s_ringMaterial;

	private static Material s_dashMaterial;

	private static Material s_sphereFillMaterial;

	private static Vector3[] s_localCircle;

	private static TMP_FontAsset s_font;

	private static Sprite s_panelSprite;

	private static int s_pieceRayMask = -1;

	private static List<(string key, string label, Piece.ComfortGroup group, int comfort)> s_allComfortTypes;

	private static readonly List<Piece> s_scanBuffer = new List<Piece>();

	private static readonly List<Piece> s_activeBuffer = new List<Piece>();

	private static readonly List<Piece> s_visibleBuffer = new List<Piece>();

	private static readonly List<Piece> s_staleBuffer = new List<Piece>();

	private ConfigEntry<KeyboardShortcut> toggleKey;

	private ConfigEntry<KeyboardShortcut> menuKey;

	private ConfigEntry<KeyboardShortcut> soloKey;

	private bool visible;

	private bool menuOpen;

	private DisplayMode mode;

	private MenuTab activeTab;

	private bool sphereMode;

	private float refreshTimer;

	private int menuIndex;

	private const int ListColumnCount = 3;

	private GameObject menuRoot;

	private TMP_Text[] modeTexts;

	private TMP_Text shapeText;

	private TMP_Text tabsText;

	private GameObject itemsSection;

	private GameObject nearbySection;

	private Transform[] listColumns;

	private Transform[] nearbyColumns;

	private readonly List<TMP_Text> listRowTexts = new List<TMP_Text>();

	private readonly List<int> listRowItemIndex = new List<int>();

	private readonly List<int> listNavToDataIndex = new List<int>();

	private int listRowsBuiltFor = -1;

	private readonly List<TMP_Text> nearbyRowTexts = new List<TMP_Text>();

	private readonly List<Piece> nearbyNavPieces = new List<Piece>();

	private readonly HashSet<Piece> lastNearbySet = new HashSet<Piece>();

	private float navRepeatTimer;

	private const float NavInitialDelay = 0.35f;

	private const float NavRepeatInterval = 0.08f;

	private readonly Dictionary<Piece, RingEntry> activeRings = new Dictionary<Piece, RingEntry>();

	private readonly HashSet<Piece> pinned = new HashSet<Piece>();

	private readonly HashSet<string> selectedTypes = new HashSet<string>();

	private Piece hoveredPiece;

	private const int ShapeRowIndex = 2;

	private const int TabRowIndex = 3;

	private const int FirstListRowIndex = 4;

	private static readonly Piece.ComfortGroup[] GroupOrder = new Piece.ComfortGroup[7]
	{
		Piece.ComfortGroup.Fire,
		Piece.ComfortGroup.Chair,
		Piece.ComfortGroup.Table,
		Piece.ComfortGroup.Bed,
		Piece.ComfortGroup.Banner,
		Piece.ComfortGroup.Carpet,
		Piece.ComfortGroup.None
	};

	private static Color CategoryColor(Piece.ComfortGroup group)
	{
		return group switch
		{
			Piece.ComfortGroup.Fire => new Color32(227, 154, 59, byte.MaxValue),
			Piece.ComfortGroup.Chair => new Color32(199, 95, 50, byte.MaxValue),
			Piece.ComfortGroup.Table => new Color32(138, 118, 72, byte.MaxValue),
			Piece.ComfortGroup.Bed => new Color32(156, 138, 192, byte.MaxValue),
			Piece.ComfortGroup.Banner => new Color32(76, 134, 168, byte.MaxValue),
			Piece.ComfortGroup.Carpet => new Color32(127, 166, 80, byte.MaxValue),
			_ => new Color32(138, 134, 114, byte.MaxValue),
		};
	}

	private void Awake()
	{
		toggleKey = base.Config.Bind("General", "Toggle Radius", new KeyboardShortcut(KeyCode.F6), "Key used to toggle the comfort radius rings on and off.");
		menuKey = base.Config.Bind("General", "Toggle Menu", new KeyboardShortcut(KeyCode.F7), "Key used to open the comfort radius selection menu.");
		soloKey = base.Config.Bind("General", "Pin Looked-At Item", new KeyboardShortcut(KeyCode.F8), "Key used to pin or unpin the exact comfort item you're looking at.");
		s_ringMaterial = new Material(Shader.Find("Sprites/Default"));
		Texture2D texture2D = new Texture2D(4, 1, TextureFormat.RGBA32, mipChain: false);
		texture2D.wrapMode = TextureWrapMode.Repeat;
		texture2D.filterMode = FilterMode.Point;
		texture2D.SetPixels(new Color[4]
		{
			Color.white,
			Color.white,
			new Color(1f, 1f, 1f, 0f),
			new Color(1f, 1f, 1f, 0f)
		});
		texture2D.Apply();
		s_dashMaterial = new Material(Shader.Find("Sprites/Default"));
		s_dashMaterial.mainTexture = texture2D;
		s_dashMaterial.mainTextureScale = new Vector2(10f, 1f);
		s_sphereFillMaterial = new Material(Shader.Find("Sprites/Default"));
		s_localCircle = new Vector3[49];
		for (int i = 0; i <= 48; i++)
		{
			float f = (float)i * Mathf.PI * 2f / 48f;
			s_localCircle[i] = new Vector3(Mathf.Cos(f) * 10f, 0.05f, Mathf.Sin(f) * 10f);
		}
	}

	private void Update()
	{
		if (toggleKey.Value.IsDown())
		{
			visible = !visible;
			if (!visible)
			{
				ClearRings();
			}
		}
		if (soloKey.Value.IsDown())
		{
			TogglePin(RaycastForComfortPiece());
		}
		if (menuKey.Value.IsDown() || (menuOpen && Input.GetKeyDown(KeyCode.Escape)))
		{
			menuOpen = !menuOpen;
			if (menuOpen)
			{
				BuildMenu();
				visible = true;
				menuIndex = 0;
			}
			if (menuRoot != null)
			{
				menuRoot.SetActive(menuOpen);
			}
		}
		if (menuOpen)
		{
			UpdateMenuInput();
			UpdateMenuVisuals();
		}
		if (!visible)
		{
			return;
		}
		Player localPlayer = Player.m_localPlayer;
		if (localPlayer == null)
		{
			ClearRings();
			return;
		}
		refreshTimer -= Time.deltaTime;
		if (refreshTimer <= 0f)
		{
			refreshTimer = 1f;
			RefreshRings(localPlayer);
			if (menuRoot != null && NearbySetChanged())
			{
				RebuildNearbyRows(localPlayer);
			}
		}
		UpdateHover();
	}

	private void TogglePin(Piece target)
	{
		if (!(target == null))
		{
			if (!pinned.Add(target))
			{
				pinned.Remove(target);
			}
			mode = DisplayMode.Picker;
			visible = true;
			refreshTimer = 0f;
		}
	}

	private static int PieceRayMask()
	{
		if (s_pieceRayMask == -1)
		{
			s_pieceRayMask = LayerMask.GetMask("Default", "static_solid", "Default_small", "piece", "piece_nonsolid", "terrain", "vehicle");
		}
		return s_pieceRayMask;
	}

	private static Piece RaycastForComfortPiece()
	{
		Transform transform = ((GameCamera.instance != null) ? GameCamera.instance.transform : null);
		Player localPlayer = Player.m_localPlayer;
		if (transform == null || localPlayer == null)
		{
			return null;
		}
		if (Physics.Raycast(transform.position, transform.forward, out var hitInfo, 50f, PieceRayMask()))
		{
			Piece componentInParent = hitInfo.collider.GetComponentInParent<Piece>();
			if (IsRealComfortPiece(componentInParent))
			{
				return componentInParent;
			}
		}
		s_scanBuffer.Clear();
		Piece.GetAllComfortPiecesInRadius(localPlayer.transform.position, 20f, s_scanBuffer);
		Piece result = null;
		float num = 1.2f;
		foreach (Piece item in s_scanBuffer)
		{
			if (!IsRealComfortPiece(item))
			{
				continue;
			}
			float num2 = Vector3.Dot(item.transform.position - transform.position, transform.forward);
			if (!(num2 <= 0f) && !(num2 > 50f))
			{
				float num3 = Vector3.Distance(transform.position + transform.forward * num2, item.transform.position);
				if (num3 < num)
				{
					num = num3;
					result = item;
				}
			}
		}
		return result;
	}

	private int MenuRowCount()
	{
		return 4 + ((activeTab == MenuTab.Items) ? listNavToDataIndex.Count : nearbyNavPieces.Count);
	}

	private void UpdateMenuInput()
	{
		int num = MenuRowCount();
		menuIndex = Mathf.Clamp(menuIndex, 0, num - 1);
		int num2 = 0;
		if (Input.GetKeyDown(KeyCode.DownArrow))
		{
			num2 = 1;
			navRepeatTimer = 0.35f;
		}
		else if (Input.GetKeyDown(KeyCode.UpArrow))
		{
			num2 = -1;
			navRepeatTimer = 0.35f;
		}
		else if (Input.GetKey(KeyCode.DownArrow) || Input.GetKey(KeyCode.UpArrow))
		{
			navRepeatTimer -= Time.unscaledDeltaTime;
			if (navRepeatTimer <= 0f)
			{
				num2 = (Input.GetKey(KeyCode.DownArrow) ? 1 : (-1));
				navRepeatTimer = 0.08f;
			}
		}
		if (num2 != 0)
		{
			menuIndex = (menuIndex + num2 + num) % num;
		}
		bool keyDown = Input.GetKeyDown(KeyCode.RightArrow);
		bool keyDown2 = Input.GetKeyDown(KeyCode.LeftArrow);
		if (menuIndex == 3)
		{
			if (keyDown || keyDown2)
			{
				activeTab = ((activeTab == MenuTab.Items) ? MenuTab.Nearby : MenuTab.Items);
			}
		}
		else if (keyDown)
		{
			ActivateMenuRow(menuIndex);
		}
	}

	private void ActivateMenuRow(int index)
	{
		switch (index)
		{
		case 0:
			ToggleMode(DisplayMode.All);
			return;
		case 1:
			ToggleMode(DisplayMode.ActiveOnly);
			return;
		case 2:
			sphereMode = !sphereMode;
			refreshTimer = 0f;
			return;
		}
		int num = index - 4;
		if (activeTab == MenuTab.Nearby)
		{
			if (num >= 0 && num < nearbyNavPieces.Count)
			{
				TogglePin(nearbyNavPieces[num]);
			}
		}
		else if (s_allComfortTypes != null && num >= 0 && num < listNavToDataIndex.Count)
		{
			string item = s_allComfortTypes[listNavToDataIndex[num]].key;
			if (!selectedTypes.Add(item))
			{
				selectedTypes.Remove(item);
			}
			mode = DisplayMode.Picker;
			visible = true;
			refreshTimer = 0f;
		}
	}

	private void ToggleMode(DisplayMode targetMode)
	{
		if (mode == targetMode && visible)
		{
			visible = false;
			ClearRings();
		}
		else
		{
			mode = targetMode;
			visible = true;
			refreshTimer = 0f;
		}
	}

	private static string GroupLabel(Piece.ComfortGroup group)
	{
		if (group != Piece.ComfortGroup.None)
		{
			return group.ToString();
		}
		return "Other";
	}

	private static bool IsRealComfortPiece(Piece piece)
	{
		return piece != null && piece.m_comfort > 0;
	}

	private static bool EnsureAllComfortTypes()
	{
		if (s_allComfortTypes != null)
		{
			return true;
		}
		if (ZNetScene.instance == null)
		{
			return false;
		}
		List<(string, string, Piece.ComfortGroup, int)> list = new List<(string, string, Piece.ComfortGroup, int)>();
		HashSet<string> hashSet = new HashSet<string>();
		foreach (GameObject prefab in ZNetScene.instance.m_prefabs)
		{
			Piece component = prefab.GetComponent<Piece>();
			if (IsRealComfortPiece(component) && hashSet.Add(component.m_name))
			{
				list.Add((component.m_name, Localization.instance.Localize(component.m_name), component.m_comfortGroup, component.m_comfort));
			}
		}
		list.Sort(delegate((string key, string label, Piece.ComfortGroup group, int comfort) a, (string key, string label, Piece.ComfortGroup group, int comfort) b)
		{
			int num = Array.IndexOf(GroupOrder, a.group).CompareTo(Array.IndexOf(GroupOrder, b.group));
			return (num == 0) ? string.CompareOrdinal(a.label, b.label) : num;
		});
		s_allComfortTypes = list;
		return true;
	}

	private static bool EnsureFont()
	{
		if (s_font != null)
		{
			return true;
		}
		s_font = Resources.FindObjectsOfTypeAll<TMP_FontAsset>().FirstOrDefault((TMP_FontAsset f) => f.name == "Valheim-AveriaSerifLibre");
		return s_font != null;
	}

	private static bool EnsurePanelSprite()
	{
		if (s_panelSprite != null)
		{
			return true;
		}
		s_panelSprite = Resources.FindObjectsOfTypeAll<Sprite>().FirstOrDefault((Sprite s) => s.name == "woodpanel_crafting");
		return s_panelSprite != null;
	}

	private void BuildMenu()
	{
		if (!(menuRoot != null) && EnsureFont() && EnsurePanelSprite())
		{
			menuRoot = new GameObject("ComfortViewMenu");
			Canvas canvas = menuRoot.AddComponent<Canvas>();
			canvas.renderMode = RenderMode.ScreenSpaceOverlay;
			canvas.sortingOrder = 9990;
			menuRoot.AddComponent<CanvasScaler>().uiScaleMode = CanvasScaler.ScaleMode.ConstantPixelSize;
			GameObject obj = new GameObject("Panel", typeof(RectTransform));
			obj.transform.SetParent(menuRoot.transform, worldPositionStays: false);
			Image image = obj.AddComponent<Image>();
			image.sprite = s_panelSprite;
			image.type = Image.Type.Sliced;
			image.color = RenderSettings.ambientLight;
			RectTransform component = obj.GetComponent<RectTransform>();
			component.anchorMin = new Vector2(0f, 1f);
			component.anchorMax = new Vector2(0f, 1f);
			component.pivot = new Vector2(0f, 1f);
			component.anchoredPosition = new Vector2(20f, -20f);
			component.sizeDelta = new Vector2(620f, 0f);
			VerticalLayoutGroup verticalLayoutGroup = obj.AddComponent<VerticalLayoutGroup>();
			verticalLayoutGroup.padding = new RectOffset(12, 12, 12, 12);
			verticalLayoutGroup.spacing = 3f;
			verticalLayoutGroup.childForceExpandWidth = true;
			verticalLayoutGroup.childForceExpandHeight = false;
			verticalLayoutGroup.childAlignment = TextAnchor.UpperLeft;
			obj.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;
			CreateText(component, "ComfortView Reforged", TextAlignmentOptions.Left, 15f, Color.white);
			CreateText(component, "Up/Down move * Right select/pin/toggle * Esc close", TextAlignmentOptions.Left, 9f, new Color(0.8f, 0.8f, 0.8f));
			modeTexts = new TMP_Text[2];
			for (int i = 0; i < modeTexts.Length; i++)
			{
				modeTexts[i] = CreateText(component, "", TextAlignmentOptions.Left, 11f, Color.white);
			}
			shapeText = CreateText(component, "", TextAlignmentOptions.Left, 11f, Color.white);
			tabsText = CreateText(component, "", TextAlignmentOptions.Left, 11f, Color.white);
			itemsSection = BuildColumnSection(component, out listColumns);
			nearbySection = BuildColumnSection(component, out nearbyColumns);
			EnsureAllComfortTypes();
		}
	}

	private static GameObject BuildColumnSection(Transform parent, out Transform[] columns)
	{
		GameObject gameObject = new GameObject("Section", typeof(RectTransform));
		gameObject.transform.SetParent(parent, worldPositionStays: false);
		HorizontalLayoutGroup horizontalLayoutGroup = gameObject.AddComponent<HorizontalLayoutGroup>();
		horizontalLayoutGroup.spacing = 14f;
		horizontalLayoutGroup.childForceExpandWidth = true;
		horizontalLayoutGroup.childForceExpandHeight = false;
		horizontalLayoutGroup.childAlignment = TextAnchor.UpperLeft;
		columns = new Transform[3];
		for (int i = 0; i < 3; i++)
		{
			GameObject gameObject2 = new GameObject("Col" + i, typeof(RectTransform));
			gameObject2.transform.SetParent(gameObject.transform, worldPositionStays: false);
			VerticalLayoutGroup verticalLayoutGroup = gameObject2.AddComponent<VerticalLayoutGroup>();
			verticalLayoutGroup.childForceExpandWidth = true;
			verticalLayoutGroup.childForceExpandHeight = false;
			verticalLayoutGroup.spacing = 1f;
			columns[i] = gameObject2.transform;
		}
		return gameObject;
	}

	private static TMP_Text CreateText(Transform parent, string text, TextAlignmentOptions alignment, float fontSize, Color color)
	{
		GameObject obj = new GameObject("Text", typeof(RectTransform));
		obj.transform.SetParent(parent, worldPositionStays: false);
		TextMeshProUGUI textMeshProUGUI = obj.AddComponent<TextMeshProUGUI>();
		textMeshProUGUI.font = s_font;
		textMeshProUGUI.fontSharedMaterial = s_font.material;
		textMeshProUGUI.fontSize = fontSize;
		textMeshProUGUI.color = color;
		textMeshProUGUI.text = text;
		textMeshProUGUI.alignment = alignment;
		textMeshProUGUI.textWrappingMode = TextWrappingModes.NoWrap;
		return textMeshProUGUI;
	}

	private void UpdateMenuVisuals()
	{
		UpdateModeRowText(0, "Show all nearby");
		UpdateModeRowText(1, "Show active only");
		bool flag = menuIndex == 2;
		shapeText.text = (flag ? "> " : "  ") + "Shape: " + (sphereMode ? "Spheres" : "Circles");
		shapeText.color = (flag ? Color.yellow : Color.white);
		bool flag2 = menuIndex == 3;
		string text = ((activeTab == MenuTab.Items) ? "[ Items ]   Nearby" : "  Items   [ Nearby ]");
		tabsText.text = (flag2 ? "> " : "  ") + text;
		tabsText.color = (flag2 ? Color.yellow : Color.white);
		itemsSection.SetActive(activeTab == MenuTab.Items);
		nearbySection.SetActive(activeTab == MenuTab.Nearby);
		EnsureAllComfortTypes();
		if (s_allComfortTypes == null)
		{
			return;
		}
		if (listRowsBuiltFor != s_allComfortTypes.Count)
		{
			RebuildListRows();
		}
		for (int i = 0; i < listRowTexts.Count; i++)
		{
			int num = listRowItemIndex[i];
			if (num >= 0)
			{
				(string, string, Piece.ComfortGroup, int) tuple = s_allComfortTypes[listNavToDataIndex[num]];
				bool flag3 = selectedTypes.Contains(tuple.Item1);
				bool flag4 = activeTab == MenuTab.Items && menuIndex == 4 + num;
				listRowTexts[i].text = (flag4 ? "> " : "  ") + (flag3 ? "[x] " : "[ ] ") + $"{tuple.Item2} (+{tuple.Item4})";
				listRowTexts[i].color = (flag4 ? Color.yellow : CategoryColor(tuple.Item3));
			}
		}
		for (int j = 0; j < nearbyRowTexts.Count; j++)
		{
			if (j < nearbyNavPieces.Count)
			{
				Piece piece = nearbyNavPieces[j];
				bool flag5 = pinned.Contains(piece);
				bool flag6 = activeTab == MenuTab.Nearby && menuIndex == 4 + j;
				Player localPlayer = Player.m_localPlayer;
				float num2 = ((localPlayer != null) ? Vector3.Distance(localPlayer.transform.position, piece.transform.position) : 0f);
				nearbyRowTexts[j].text = (flag6 ? "> " : "  ") + (flag5 ? "[x] " : "[ ] ") + $"{Localization.instance.Localize(piece.m_name)} ({num2:0}m)";
				nearbyRowTexts[j].color = (flag6 ? Color.yellow : CategoryColor(piece.m_comfortGroup));
			}
		}
	}

	private void UpdateModeRowText(int index, string label)
	{
		bool flag = index == (int)mode && visible;
		bool flag2 = menuIndex == index;
		modeTexts[index].text = (flag2 ? "> " : "  ") + (flag ? "(*) " : "( ) ") + label;
		modeTexts[index].color = (flag2 ? Color.yellow : Color.white);
	}

	private static int ShortestColumn(int[] colLoad)
	{
		int num = 0;
		for (int i = 1; i < colLoad.Length; i++)
		{
			if (colLoad[i] < colLoad[num])
			{
				num = i;
			}
		}
		return num;
	}

	private void RebuildListRows()
	{
		Transform[] array = listColumns;
		for (int i = 0; i < array.Length; i++)
		{
			foreach (Transform item3 in array[i])
			{
				UnityEngine.Object.Destroy(item3.gameObject);
			}
		}
		listRowTexts.Clear();
		listRowItemIndex.Clear();
		listNavToDataIndex.Clear();
		List<List<(int, int, Piece.ComfortGroup)>> list = new List<List<(int, int, Piece.ComfortGroup)>>();
		for (int j = 0; j < listColumns.Length; j++)
		{
			list.Add(new List<(int, int, Piece.ComfortGroup)>());
		}
		int[] array2 = new int[listColumns.Length];
		int num = 0;
		while (num < s_allComfortTypes.Count)
		{
			Piece.ComfortGroup item = s_allComfortTypes[num].group;
			int k;
			for (k = num; k < s_allComfortTypes.Count && s_allComfortTypes[k].group == item; k++)
			{
			}
			int num2 = ShortestColumn(array2);
			array2[num2] += 1 + (k - num);
			list[num2].Add((num, k, item));
			num = k;
		}
		for (int l = 0; l < listColumns.Length; l++)
		{
			foreach (var item4 in list[l])
			{
				TMP_Text item2 = CreateText(listColumns[l], GroupLabel(item4.Item3), TextAlignmentOptions.Left, 10f, CategoryColor(item4.Item3));
				listRowTexts.Add(item2);
				listRowItemIndex.Add(-1);
				var (m, _, _) = item4;
				for (; m < item4.Item2; m++)
				{
					listRowTexts.Add(CreateText(listColumns[l], "", TextAlignmentOptions.Left, 10f, Color.white));
					listRowItemIndex.Add(listNavToDataIndex.Count);
					listNavToDataIndex.Add(m);
				}
			}
		}
		listRowsBuiltFor = s_allComfortTypes.Count;
	}

	private bool NearbySetChanged()
	{
		if (s_scanBuffer.Count != lastNearbySet.Count)
		{
			return true;
		}
		foreach (Piece item in s_scanBuffer)
		{
			if (!lastNearbySet.Contains(item))
			{
				return true;
			}
		}
		return false;
	}

	private void RebuildNearbyRows(Player player)
	{
		lastNearbySet.Clear();
		lastNearbySet.UnionWith(s_scanBuffer);
		Transform[] array = nearbyColumns;
		for (int i = 0; i < array.Length; i++)
		{
			foreach (Transform item in array[i])
			{
				UnityEngine.Object.Destroy(item.gameObject);
			}
		}
		nearbyRowTexts.Clear();
		nearbyNavPieces.Clear();
		if (s_scanBuffer.Count == 0)
		{
			CreateText(nearbyColumns[0], "(none in range)", TextAlignmentOptions.Left, 10f, new Color(0.7f, 0.7f, 0.7f));
			return;
		}
		List<Piece> list = s_scanBuffer.OrderBy((Piece p) => Vector3.Distance(player.transform.position, p.transform.position)).ToList();
		List<List<Piece>> list2 = new List<List<Piece>>();
		for (int num = 0; num < nearbyColumns.Length; num++)
		{
			list2.Add(new List<Piece>());
		}
		int[] array2 = new int[nearbyColumns.Length];
		foreach (Piece item2 in list)
		{
			int num2 = ShortestColumn(array2);
			array2[num2]++;
			list2[num2].Add(item2);
		}
		for (int num3 = 0; num3 < nearbyColumns.Length; num3++)
		{
			foreach (Piece item3 in list2[num3])
			{
				nearbyRowTexts.Add(CreateText(nearbyColumns[num3], "", TextAlignmentOptions.Left, 10f, Color.white));
				nearbyNavPieces.Add(item3);
			}
		}
	}

	private void UpdateHover()
	{
		Piece piece = null;
		Transform transform = ((GameCamera.instance != null) ? GameCamera.instance.transform : null);
		if (transform != null && Physics.Raycast(transform.position, transform.forward, out var hitInfo, 50f, PieceRayMask()))
		{
			Piece componentInParent = hitInfo.collider.GetComponentInParent<Piece>();
			if (componentInParent != null && activeRings.ContainsKey(componentInParent))
			{
				piece = componentInParent;
			}
		}
		if (!(piece == hoveredPiece))
		{
			if (hoveredPiece != null && activeRings.TryGetValue(hoveredPiece, out var value))
			{
				SetHover(value, isHovered: false);
			}
			hoveredPiece = piece;
			if (hoveredPiece != null && activeRings.TryGetValue(hoveredPiece, out var value2))
			{
				SetHover(value2, isHovered: true);
			}
		}
	}

	private void RefreshRings(Player player)
	{
		s_scanBuffer.Clear();
		Piece.GetAllComfortPiecesInRadius(player.transform.position, 20f, s_scanBuffer);
		s_scanBuffer.RemoveAll((Piece p) => !IsRealComfortPiece(p));
		pinned.RemoveWhere((Piece p) => p == null);
		s_visibleBuffer.Clear();
		switch (mode)
		{
		case DisplayMode.ActiveOnly:
			ComputeActiveSet(s_scanBuffer, player.transform.position, s_visibleBuffer);
			break;
		case DisplayMode.Picker:
			foreach (Piece item in s_scanBuffer)
			{
				if (pinned.Contains(item) || selectedTypes.Contains(item.m_name))
				{
					s_visibleBuffer.Add(item);
				}
			}
			break;
		default:
			s_visibleBuffer.AddRange(s_scanBuffer);
			break;
		}
		s_staleBuffer.Clear();
		foreach (KeyValuePair<Piece, RingEntry> activeRing in activeRings)
		{
			if (activeRing.Key == null || !s_visibleBuffer.Contains(activeRing.Key))
			{
				s_staleBuffer.Add(activeRing.Key);
			}
		}
		foreach (Piece item2 in s_staleBuffer)
		{
			if (activeRings.TryGetValue(item2, out var value) && value.Root != null)
			{
				UnityEngine.Object.Destroy(value.Root);
			}
			activeRings.Remove(item2);
			if (hoveredPiece == item2)
			{
				hoveredPiece = null;
			}
		}
		foreach (Piece item3 in s_visibleBuffer)
		{
			if (!(item3 == null) && !activeRings.ContainsKey(item3))
			{
				activeRings[item3] = CreateRing(item3.transform, CategoryColor(item3.m_comfortGroup));
			}
		}
		foreach (KeyValuePair<Piece, RingEntry> activeRing2 in activeRings)
		{
			activeRing2.Value.SphereRoot.SetActive(sphereMode);
		}
	}

	private static void ComputeActiveSet(List<Piece> nearby, Vector3 playerPos, List<Piece> output)
	{
		s_activeBuffer.Clear();
		foreach (Piece item in nearby)
		{
			if (item != null && Vector3.Distance(item.transform.position, playerPos) < 10f)
			{
				s_activeBuffer.Add(item);
			}
		}
		s_activeBuffer.Sort(CompareComfort);
		for (int i = 0; i < s_activeBuffer.Count; i++)
		{
			Piece piece = s_activeBuffer[i];
			if (i > 0)
			{
				Piece piece2 = s_activeBuffer[i - 1];
				if ((piece.m_comfortGroup != Piece.ComfortGroup.None && piece.m_comfortGroup == piece2.m_comfortGroup) || piece.m_name == piece2.m_name)
				{
					continue;
				}
			}
			output.Add(piece);
		}
	}

	private static int CompareComfort(Piece x, Piece y)
	{
		if (x.m_comfortGroup != y.m_comfortGroup)
		{
			return x.m_comfortGroup.CompareTo(y.m_comfortGroup);
		}
		float num = x.GetComfort();
		float num2 = y.GetComfort();
		if (num != num2)
		{
			return num2.CompareTo(num);
		}
		return string.CompareOrdinal(y.m_name, x.m_name);
	}

	private static RingEntry CreateRing(Transform parent, Color color)
	{
		GameObject gameObject = new GameObject("ComfortRing");
		gameObject.transform.SetParent(parent, worldPositionStays: false);
		gameObject.transform.localPosition = Vector3.zero;
		gameObject.transform.localRotation = Quaternion.identity;
		LineRenderer lineRenderer = gameObject.AddComponent<LineRenderer>();
		lineRenderer.useWorldSpace = false;
		lineRenderer.loop = true;
		lineRenderer.positionCount = s_localCircle.Length;
		lineRenderer.SetPositions(s_localCircle);
		lineRenderer.material = s_ringMaterial;
		lineRenderer.shadowCastingMode = ShadowCastingMode.Off;
		lineRenderer.receiveShadows = false;
		GameObject gameObject2 = new GameObject("Post");
		gameObject2.transform.SetParent(gameObject.transform, worldPositionStays: false);
		LineRenderer lineRenderer2 = gameObject2.AddComponent<LineRenderer>();
		lineRenderer2.useWorldSpace = false;
		lineRenderer2.loop = false;
		lineRenderer2.positionCount = 2;
		lineRenderer2.SetPositions(new Vector3[2]
		{
			Vector3.zero,
			Vector3.up * 2.5f
		});
		lineRenderer2.widthMultiplier = 0.04f;
		lineRenderer2.material = s_ringMaterial;
		lineRenderer2.shadowCastingMode = ShadowCastingMode.Off;
		lineRenderer2.receiveShadows = false;
		gameObject2.SetActive(value: false);
		GameObject obj = new GameObject("Spoke");
		obj.transform.SetParent(gameObject.transform, worldPositionStays: false);
		LineRenderer lineRenderer3 = obj.AddComponent<LineRenderer>();
		lineRenderer3.useWorldSpace = false;
		lineRenderer3.loop = false;
		lineRenderer3.positionCount = 2;
		lineRenderer3.SetPositions(new Vector3[2]
		{
			Vector3.zero,
			new Vector3(10f, 0.05f, 0f)
		});
		lineRenderer3.widthMultiplier = 0.05f;
		lineRenderer3.material = s_dashMaterial;
		lineRenderer3.textureMode = LineTextureMode.Tile;
		lineRenderer3.shadowCastingMode = ShadowCastingMode.Off;
		lineRenderer3.receiveShadows = false;
		Color color2 = color;
		color2.a = 0.6f;
		lineRenderer3.startColor = color2;
		lineRenderer3.endColor = color2;
		GameObject gameObject3 = new GameObject("Sphere");
		gameObject3.transform.SetParent(gameObject.transform, worldPositionStays: false);
		LineRenderer[] sphereRings = new LineRenderer[4]
		{
			CreateSphereRing(gameObject3.transform, Quaternion.Euler(90f, 0f, 0f)),
			CreateSphereRing(gameObject3.transform, Quaternion.Euler(0f, 0f, 90f)),
			CreateSphereRing(gameObject3.transform, Quaternion.Euler(45f, 0f, 45f)),
			CreateSphereRing(gameObject3.transform, Quaternion.Euler(-45f, 0f, 45f))
		};
		GameObject obj2 = GameObject.CreatePrimitive(PrimitiveType.Sphere);
		obj2.name = "SphereFill";
		obj2.transform.SetParent(gameObject3.transform, worldPositionStays: false);
		obj2.transform.localScale = Vector3.one * 20f;
		UnityEngine.Object.DestroyImmediate(obj2.GetComponent<Collider>());
		MeshRenderer component = obj2.GetComponent<MeshRenderer>();
		component.material = s_sphereFillMaterial;
		component.material.color = new Color(color.r, color.g, color.b, 0.12f);
		component.shadowCastingMode = ShadowCastingMode.Off;
		component.receiveShadows = false;
		gameObject3.SetActive(value: false);
		RingEntry obj3 = new RingEntry
		{
			Root = gameObject,
			Ring = lineRenderer,
			Post = lineRenderer2,
			Spoke = lineRenderer3,
			SphereRoot = gameObject3,
			SphereRings = sphereRings,
			Color = color
		};
		SetHover(obj3, isHovered: false);
		return obj3;
	}

	private static LineRenderer CreateSphereRing(Transform parent, Quaternion localRotation)
	{
		GameObject obj = new GameObject("SphereRing");
		obj.transform.SetParent(parent, worldPositionStays: false);
		obj.transform.localRotation = localRotation;
		LineRenderer lineRenderer = obj.AddComponent<LineRenderer>();
		lineRenderer.useWorldSpace = false;
		lineRenderer.loop = true;
		lineRenderer.positionCount = s_localCircle.Length;
		lineRenderer.SetPositions(s_localCircle);
		lineRenderer.material = s_ringMaterial;
		lineRenderer.shadowCastingMode = ShadowCastingMode.Off;
		lineRenderer.receiveShadows = false;
		return lineRenderer;
	}

	private static void SetHover(RingEntry entry, bool isHovered)
	{
		Color color = entry.Color;
		color.a = (isHovered ? 1f : 0.35f);
		float widthMultiplier = (isHovered ? 0.12f : 0.04f);
		entry.Ring.widthMultiplier = widthMultiplier;
		entry.Ring.startColor = color;
		entry.Ring.endColor = color;
		LineRenderer[] sphereRings = entry.SphereRings;
		foreach (LineRenderer obj in sphereRings)
		{
			obj.widthMultiplier = widthMultiplier;
			obj.startColor = color;
			obj.endColor = color;
		}
		Color color2 = entry.Color;
		color2.a = 1f;
		entry.Post.startColor = color2;
		entry.Post.endColor = color2;
		entry.Post.gameObject.SetActive(isHovered);
	}

	private void ClearRings()
	{
		foreach (RingEntry value in activeRings.Values)
		{
			if (value.Root != null)
			{
				UnityEngine.Object.Destroy(value.Root);
			}
		}
		activeRings.Clear();
		hoveredPiece = null;
	}
}
