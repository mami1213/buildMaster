using UnityEditor;
using UnityEngine;
using System.Collections.Generic;
using System;
using System.Linq;

/// <summary>
/// Prefabとそのプレビュー用の色を格納するデータクラス。
/// </summary>
[System.Serializable]
public class PrefabColorData
{
    // 対象となるPrefabのGameObject。
    public GameObject prefab;
    // エディタ上で表示するためのプレビューカラー。
    public Color previewColor = Color.white;
}

/// <summary>
/// 1つのタイル（床や壁）が持つ詳細なセグメント情報を管理するクラス。
/// 各タイルは内部的に4x4=16のセグメントに分割して、より細かい編集を可能にする。
/// </summary>
[System.Serializable]
public class SegmentData
{
    // 4x4=16個のサブセグメントそれぞれにどのPrefabが割り当てられているかを示すインデックス配列。
    // -1: 空、0以上: Prefabのインデックス、-2以下: マージされたセルの子（-(親のインデックス + 2)で格納）。
    public int[] segmentPrefabIndices = new int[16];
    // 詳細編集ウィンドウで編集されたかどうかを示すフラグ。
    public bool isDetailed = false;
    // このセグメントにドアが設置されている場合の、ドアPrefabのインデックス。
    public int doorPrefabIndex = -1;
    // ドアが設置されている場合の、4x4グリッド内での矩形範囲。
    public RectInt doorRect;

    /// <summary>
    /// コンストラクタ。すべてのセグメントを「空」(-1)で初期化する。
    /// </summary>
    public SegmentData()
    {
        for (int i = 0; i < segmentPrefabIndices.Length; i++)
        {
            segmentPrefabIndices[i] = -1;
        }
    }

    /// <summary>
    /// このセグメントに何らかのデータ（壁、床、ドア）が設定されているかを判定する。
    /// </summary>
    /// <returns>アクティブな場合はtrue。</returns>
    public bool IsActive()
    {
        if (doorPrefabIndex != -1) return true;
        for (int i = 0; i < segmentPrefabIndices.Length; i++)
        {
            if (segmentPrefabIndices[i] != -1) return true;
        }
        return false;
    }

    /// <summary>
    /// 全ての16セグメントを指定されたPrefabインデックスで塗りつぶす。
    /// これにより、タイル全体が単一のPrefabで構成される簡易設定状態になる。
    /// </summary>
    /// <param name="prefabIndex">割り当てるPrefabのインデックス。</param>
    public void FillAllSegments(int prefabIndex)
    {
        for (int i = 0; i < segmentPrefabIndices.Length; i++)
        {
            segmentPrefabIndices[i] = prefabIndex;
        }
        // 詳細編集フラグは下ろす。
        isDetailed = false;
        // ドア情報もクリアする。
        ClearDoor();
    }

    /// <summary>
    /// ドアの情報を設定する。
    /// </summary>
    /// <param name="doorIndex">ドアPrefabのインデックス。</param>
    /// <param name="rect">ドアの矩形範囲。</param>
    public void SetDoor(int doorIndex, RectInt rect)
    {
        isDetailed = true; // ドア設定は詳細設定とみなす。
        doorPrefabIndex = doorIndex;
        doorRect = rect;
    }

    /// <summary>
    /// ドアの情報をクリアする。
    /// </summary>
    public void ClearDoor()
    {
        doorPrefabIndex = -1;
        doorRect = new RectInt(0, 0, 0, 0);
    }
}

/// <summary>
/// 床や壁の詳細編集を行うエディタウィンドウの基底クラス。
/// 4x4のグリッドUIと、セグメントのペイント、消去、マージ機能を提供する。
/// </summary>
public abstract class DetailEditorWindowBase : EditorWindow
{
    // 編集対象のセグメントデータ。
    protected SegmentData targetData;
    // メインのエディタウィンドウへの参照。
    protected buildMaster mainEditor;

    // 定数定義
    protected const int SEGMENTS_PER_SIDE = 4; // グリッドの一辺のセグメント数
    protected const float DETAIL_CELL_SIZE = 60f; // 詳細編集グリッドのセルサイズ
    protected const float DETAIL_GRID_PADDING = 10f; // グリッドのパディング

    // ドラッグ操作のモードを定義する列挙型。
    protected enum DetailDragMode { None, Painting, Erasing, Merging }
    protected DetailDragMode dragMode = DetailDragMode.None;

    // マージ（矩形選択）操作用の変数。
    private Vector2Int mergeStartPoint;
    private RectInt mergeSelectionRect;

    // 派生クラスで実装される、Prefabリストを取得するための抽象メソッド。
    protected abstract List<PrefabColorData> GetPrefabList();
    // 派生クラスで実装される、選択中のPrefabインデックスを取得するための抽象メソッド。
    protected abstract int GetSelectedPrefabIndex();

    /// <summary>
    /// ウィンドウ上部に、現在選択されているPrefabの情報を表示する。
    /// </summary>
    protected virtual void DrawHeader()
    {
        EditorGUILayout.BeginVertical("box");
        EditorGUILayout.LabelField("選択中のPrefab", EditorStyles.miniBoldLabel);
        int selectedIndex = GetSelectedPrefabIndex();
        var prefabList = GetPrefabList();

        if (selectedIndex >= 0 && selectedIndex < prefabList.Count)
        {
            var prefabData = prefabList[selectedIndex];
            EditorGUILayout.BeginHorizontal();
            Color color = prefabData.previewColor;
            color.a = 1f; // 透明度を1に
            EditorGUI.DrawRect(GUILayoutUtility.GetRect(20, 20), color);
            EditorGUILayout.LabelField(prefabData.prefab != null ? prefabData.prefab.name : "(Prefabがありません)");
            EditorGUILayout.EndHorizontal();
        }
        else
        {
            EditorGUILayout.LabelField("なし (消しゴムモード)");
        }
        EditorGUILayout.EndVertical();
        EditorGUILayout.Space();
    }

    /// <summary>
    /// Unityエディタウィンドウのメイン描画ループ。
    /// </summary>
    private void OnGUI()
    {
        if (targetData == null || mainEditor == null)
        {
            EditorGUILayout.LabelField("表示するデータがありません。このウィンドウを閉じて再度開いてください。");
            return;
        }

        Event e = Event.current; // 現在のイベントを取得

        EditorGUILayout.BeginVertical(new GUIStyle { padding = new RectOffset(10, 10, 10, 10) });

        DrawHeader(); // ヘッダー描画
        DrawEditorSpecificControls(); // 派生クラス固有のUIを描画

        GUILayout.FlexibleSpace(); // 中央に寄せるためのスペーサー

        // グリッド描画領域を確保
        Rect gridArea = GUILayoutUtility.GetRect(DETAIL_CELL_SIZE * SEGMENTS_PER_SIDE, DETAIL_CELL_SIZE * SEGMENTS_PER_SIDE, GUILayout.ExpandWidth(false), GUILayout.ExpandHeight(false));
        gridArea.x = (position.width - gridArea.width) / 2; // 中央に配置

        // マウスボタンが離された時の処理
        if (e.type == EventType.MouseUp)
        {
            // マージモードだったら、矩形選択を確定してマージ処理を呼び出す
            if (dragMode == DetailDragMode.Merging)
            {
                if (mergeSelectionRect.width > 0 && mergeSelectionRect.height > 0)
                {
                    HandleMerge(mergeSelectionRect);
                }
            }
            // ドラッグモードを解除し、選択矩形をリセット
            dragMode = DetailDragMode.None;
            mergeSelectionRect = new RectInt(0, 0, 0, 0);
            Repaint();
        }

        DrawEditorContent(e, gridArea); // グリッド本体の描画と入力処理

        GUILayout.FlexibleSpace(); // 下部に寄せるためのスペーサー
        DrawButtons(); // OKボタンなどを描画
        EditorGUILayout.EndVertical();
    }

    /// <summary>
    /// 派生クラスが固有のUIコントロールを描画するための仮想メソッド。
    /// </summary>
    protected virtual void DrawEditorSpecificControls() { }

    /// <summary>
    /// グリッド領域のコンテンツを描画する。
    /// </summary>
    protected virtual void DrawEditorContent(Event e, Rect gridArea)
    {
        HandleGeneralInput(e, gridArea); // マウス入力全般の処理
        DrawGrid(e, gridArea);           // グリッドのセルを描画
        DrawGridLines(gridArea);         // グリッドの線を描画

        // マージモード中に選択されている矩形をハイライト表示する
        if (dragMode == DetailDragMode.Merging && mergeSelectionRect.width > 0 && mergeSelectionRect.height > 0)
        {
            Rect selectionVisual = new Rect(
                gridArea.x + mergeSelectionRect.x * DETAIL_CELL_SIZE,
                gridArea.y + mergeSelectionRect.y * DETAIL_CELL_SIZE,
                mergeSelectionRect.width * DETAIL_CELL_SIZE,
                mergeSelectionRect.height * DETAIL_CELL_SIZE);
            EditorGUI.DrawRect(selectionVisual, new Color(0.1f, 0.6f, 1.0f, 0.5f)); // 半透明の青で塗りつぶし
            Handles.color = new Color(0.1f, 0.6f, 1.0f, 0.9f);
            Handles.DrawWireCube(selectionVisual.center, selectionVisual.size); // 枠線を描画
            Handles.color = Color.white;
        }
    }

    /// <summary>
    /// ウィンドウ下部のボタンを描画する。
    /// </summary>
    protected virtual void DrawButtons()
    {
        EditorGUILayout.BeginHorizontal();
        GUILayout.FlexibleSpace();
        if (GUI.Button(new Rect(position.width / 2 - 60, position.height - 45, 120, 30), "OK"))
        {
            // OKボタンが押されたら、isDetailedフラグを更新してメインエディタを再描画し、ウィンドウを閉じる
            targetData.isDetailed = targetData.IsActive();
            mainEditor.Repaint();
            Close();
        }
        EditorGUILayout.EndHorizontal();
        GUILayout.Space(10);
    }

    /// <summary>
    /// 4x4のグリッドセルを描画する。マージされたセルは一つの大きなセルとして描画する。
    /// </summary>
    protected void DrawGrid(Event e, Rect gridArea)
    {
        bool[] drawn = new bool[SEGMENTS_PER_SIDE * SEGMENTS_PER_SIDE]; // 各セルが描画済みかを管理する配列

        for (int y = 0; y < SEGMENTS_PER_SIDE; y++)
        {
            for (int x = 0; x < SEGMENTS_PER_SIDE; x++)
            {
                int index = y * SEGMENTS_PER_SIDE + x;
                if (drawn[index]) continue; // 描画済みならスキップ

                // このセルが属するマージグループの親（左上）のインデックスを取得
                int originIndex = GetMergeOriginIndex(index);
                // 親が同じセルをすべて取得
                List<Vector2Int> groupCells = GetAllCellsInGroup(originIndex);

                // 親に設定されているPrefabインデックスから色を取得
                int prefabIndex = targetData.segmentPrefabIndices[originIndex];
                var prefabList = GetPrefabList();
                Color color = new Color(0.5f, 0.5f, 0.5f, 1f); // デフォルトは灰色

                if (prefabIndex >= 0 && prefabIndex < prefabList.Count && prefabList[prefabIndex].prefab != null)
                {
                    color = prefabList[prefabIndex].previewColor;
                }

                // グループ内の各セルを同じ色で描画（見た目上、一つの大きなセルのようにする）
                foreach (var cellPos in groupCells)
                {
                    Rect cellRect = new Rect(gridArea.x + cellPos.x * DETAIL_CELL_SIZE, gridArea.y + cellPos.y * DETAIL_CELL_SIZE, DETAIL_CELL_SIZE, DETAIL_CELL_SIZE);
                    EditorGUI.DrawRect(cellRect, color);
                }

                // 描画済みにマーク
                foreach (var cellPos in groupCells)
                {
                    drawn[cellPos.y * SEGMENTS_PER_SIDE + cellPos.x] = true;
                }
            }
        }
    }

    /// <summary>
    /// 指定された親インデックスが属するマージグループの全セルの座標リストを取得する。
    /// </summary>
    protected List<Vector2Int> GetAllCellsInGroup(int originIndex)
    {
        var groupCells = new List<Vector2Int>();
        if (originIndex < 0 || originIndex >= targetData.segmentPrefabIndices.Length) return groupCells;

        // まず親自身を追加
        groupCells.Add(new Vector2Int(originIndex % SEGMENTS_PER_SIDE, originIndex / SEGMENTS_PER_SIDE));
        // 子セルの探索用ポインタ値（-(親インデックス + 2)）
        int searchPointer = -(originIndex + 2);

        // 全セルを走査し、自分を親として指しているセルを探す
        for (int i = 0; i < targetData.segmentPrefabIndices.Length; i++)
        {
            if (targetData.segmentPrefabIndices[i] == searchPointer)
            {
                groupCells.Add(new Vector2Int(i % SEGMENTS_PER_SIDE, i / SEGMENTS_PER_SIDE));
            }
        }
        return groupCells;
    }

    /// <summary>
    /// グリッドの境界線を描画する。マージされたセル間の線は描画しない。
    /// </summary>
    protected void DrawGridLines(Rect gridArea)
    {
        Handles.color = new Color(0, 0, 0, 0.6f);
        // グリッド全体の外枠
        Handles.DrawWireCube(new Vector3(gridArea.center.x, gridArea.center.y, 0), new Vector3(gridArea.width, gridArea.height, 0));

        for (int y = 0; y < SEGMENTS_PER_SIDE; y++)
        {
            for (int x = 0; x < SEGMENTS_PER_SIDE; x++)
            {
                int currentIndex = y * SEGMENTS_PER_SIDE + x;
                // 下のセルとの境界線
                if (y < SEGMENTS_PER_SIDE - 1)
                {
                    int belowIndex = (y + 1) * SEGMENTS_PER_SIDE + x;
                    // 自分と下のセルの親が異なる（マージされていない）場合のみ線を描画
                    if (GetMergeOriginIndex(currentIndex) != GetMergeOriginIndex(belowIndex))
                    {
                        Handles.DrawLine(new Vector3(gridArea.x + x * DETAIL_CELL_SIZE, gridArea.y + (y + 1) * DETAIL_CELL_SIZE, 0), new Vector3(gridArea.x + (x + 1) * DETAIL_CELL_SIZE, gridArea.y + (y + 1) * DETAIL_CELL_SIZE, 0));
                    }
                }
                // 右のセルとの境界線
                if (x < SEGMENTS_PER_SIDE - 1)
                {
                    int rightIndex = y * SEGMENTS_PER_SIDE + (x + 1);
                    // 自分と右のセルの親が異なる場合のみ線を描画
                    if (GetMergeOriginIndex(currentIndex) != GetMergeOriginIndex(rightIndex))
                    {
                        Handles.DrawLine(new Vector3(gridArea.x + (x + 1) * DETAIL_CELL_SIZE, gridArea.y + y * DETAIL_CELL_SIZE, 0), new Vector3(gridArea.x + (x + 1) * DETAIL_CELL_SIZE, gridArea.y + (y + 1) * DETAIL_CELL_SIZE, 0));
                    }
                }
            }
        }
    }

    /// <summary>
    /// 指定されたインデックスのセルが属するマージグループの親（左上）のインデックスを返す。
    /// マージされていない場合は自身のインデックスを返す。
    /// </summary>
    private int GetMergeOriginIndex(int index)
    {
        if (index < 0 || index >= targetData.segmentPrefabIndices.Length) return -1;

        // 値が-2以下なら、それは親を指すポインタ
        if (targetData.segmentPrefabIndices[index] < -1)
        {
            // -(値 + 2) で親のインデックスを復元
            return -(targetData.segmentPrefabIndices[index] + 2);
        }
        // そうでなければ自分が親
        return index;
    }

    /// <summary>
    /// グリッド上での一般的なマウス入力を処理する。
    /// Ctrl/Cmdキーが押されている場合はマージモードとして矩形選択を処理する。
    /// </summary>
    private void HandleGeneralInput(Event e, Rect gridArea)
    {
        if (gridArea.Contains(e.mousePosition))
        {
            // Ctrl(MacではCmd)キーが押されている場合 -> マージモード
            if (e.control || e.command)
            {
                if (e.type == EventType.MouseDown && e.button == 0)
                {
                    dragMode = DetailDragMode.Merging;
                    mergeStartPoint = GetCellFromPosition(e.mousePosition, gridArea);
                    e.Use(); // イベントを消費
                }
                else if (e.type == EventType.MouseDrag && dragMode == DetailDragMode.Merging)
                {
                    Vector2Int currentPoint = GetCellFromPosition(e.mousePosition, gridArea);
                    // 開始点と現在点から矩形を計算
                    int x = Mathf.Min(mergeStartPoint.x, currentPoint.x);
                    int y = Mathf.Min(mergeStartPoint.y, currentPoint.y);
                    int width = Mathf.Abs(mergeStartPoint.x - currentPoint.x) + 1;
                    int height = Mathf.Abs(mergeStartPoint.y - currentPoint.y) + 1;
                    mergeSelectionRect = new RectInt(x, y, width, height);
                    Repaint();
                    e.Use();
                }
                return; // マージモード中は通常入力は処理しない
            }

            // 通常のペイント・消去入力処理
            for (int y = 0; y < SEGMENTS_PER_SIDE; y++)
            {
                for (int x = 0; x < SEGMENTS_PER_SIDE; x++)
                {
                    Rect cellRect = new Rect(gridArea.x + x * DETAIL_CELL_SIZE, gridArea.y + y * DETAIL_CELL_SIZE, DETAIL_CELL_SIZE, DETAIL_CELL_SIZE);
                    HandleMouseInput(e, cellRect, y * SEGMENTS_PER_SIDE + x);
                }
            }
        }
    }

    /// <summary>
    /// 個々のセルに対するペイント・消去の入力を処理する。
    /// </summary>
    protected void HandleMouseInput(Event e, Rect cellRect, int index)
    {
        if (cellRect.Contains(e.mousePosition))
        {
            if (e.shift) return; // Shiftキー中はドア設置モードなので何もしない
            int originIndex = GetMergeOriginIndex(index); // 操作対象はセルグループの親

            if (e.type == EventType.MouseDown && e.button == 0)
            {
                bool isGroupPainted = targetData.segmentPrefabIndices[originIndex] >= 0;
                // クリックしたグループが既に塗られている場合 -> 消去モード
                if (isGroupPainted)
                {
                    dragMode = DetailDragMode.Erasing;
                    targetData.segmentPrefabIndices[originIndex] = -1;
                }
                // Prefabが選択されていて、グループが塗られていない場合 -> ペイントモード
                else if (GetSelectedPrefabIndex() >= 0)
                {
                    dragMode = DetailDragMode.Painting;
                    targetData.segmentPrefabIndices[originIndex] = GetSelectedPrefabIndex();
                }
                e.Use(); Repaint();
            }
            else if (e.type == EventType.MouseDrag && e.button == 0)
            {
                // ドラッグ中の処理
                if (dragMode == DetailDragMode.Painting && GetSelectedPrefabIndex() >= 0)
                {
                    if (targetData.segmentPrefabIndices[originIndex] != GetSelectedPrefabIndex())
                    {
                        targetData.segmentPrefabIndices[originIndex] = GetSelectedPrefabIndex();
                        e.Use(); Repaint();
                    }
                }
                else if (dragMode == DetailDragMode.Erasing)
                {
                    if (targetData.segmentPrefabIndices[originIndex] >= 0)
                    {
                        targetData.segmentPrefabIndices[originIndex] = -1;
                        e.Use(); Repaint();
                    }
                }
            }
        }
    }

    /// <summary>
    /// 選択された矩形範囲のセルをマージまたはアンマージする。
    /// </summary>
    protected void HandleMerge(RectInt selectionRect)
    {
        if (selectionRect.width < 1 || selectionRect.height < 1) return;

        // 選択範囲内のセル座標をHashSetに格納
        var selectionCells = new HashSet<Vector2Int>();
        for (int y = selectionRect.yMin; y < selectionRect.yMax; y++)
        {
            for (int x = selectionRect.xMin; x < selectionRect.xMax; x++)
            {
                selectionCells.Add(new Vector2Int(x, y));
            }
        }

        // 選択範囲の左上のセルが属する既存のグループを取得
        int firstCellIndex = selectionRect.yMin * SEGMENTS_PER_SIDE + selectionRect.xMin;
        int originIndexOfFirstCell = GetMergeOriginIndex(firstCellIndex);
        var existingGroup = new HashSet<Vector2Int>(GetAllCellsInGroup(originIndexOfFirstCell));

        // 選択範囲が既存のグループと完全に一致し、かつ複数セルからなる場合 -> アンマージ処理
        if (selectionCells.SetEquals(existingGroup) && selectionCells.Count > 1)
        {
            foreach (var cellPos in existingGroup)
            {
                // 各セルを独立した空セルに戻す
                targetData.segmentPrefabIndices[cellPos.y * SEGMENTS_PER_SIDE + cellPos.x] = -1;
            }
            return;
        }

        // 以下、マージ処理
        // 選択範囲に含まれるすべての既存グループを一旦解除する
        var cellsToClear = new HashSet<int>();
        foreach (var cellPos in selectionCells)
        {
            int index = cellPos.y * SEGMENTS_PER_SIDE + cellPos.x;
            int originIndex = GetMergeOriginIndex(index);
            foreach (var groupCell in GetAllCellsInGroup(originIndex))
            {
                cellsToClear.Add(groupCell.y * SEGMENTS_PER_SIDE + groupCell.x);
            }
        }
        foreach (var index in cellsToClear)
        {
            targetData.segmentPrefabIndices[index] = -1;
        }

        // 選択範囲の左上を新しい親とする
        int newOriginIndex = selectionRect.yMin * SEGMENTS_PER_SIDE + selectionRect.xMin;

        // 選択範囲内のセルを新しいグループとして設定
        foreach (var cellPos in selectionCells)
        {
            int index = cellPos.y * SEGMENTS_PER_SIDE + cellPos.x;
            if (index == newOriginIndex)
            {
                // 親は空(-1)のまま（Prefabはまだ設定されていない）
                targetData.segmentPrefabIndices[index] = -1;
            }
            else
            {
                // 子は親を指すポインタを設定
                targetData.segmentPrefabIndices[index] = -(newOriginIndex + 2);
            }
        }
    }

    /// <summary>
    /// マウスの座標をグリッドのセル座標に変換する。
    /// </summary>
    protected Vector2Int GetCellFromPosition(Vector2 mousePos, Rect gridArea)
    {
        int x = Mathf.FloorToInt((mousePos.x - gridArea.x) / DETAIL_CELL_SIZE);
        int y = Mathf.FloorToInt((mousePos.y - gridArea.y) / DETAIL_CELL_SIZE);
        // グリッド範囲外にはみ出ないようにClamp
        return new Vector2Int(Mathf.Clamp(x, 0, SEGMENTS_PER_SIDE - 1), Mathf.Clamp(y, 0, SEGMENTS_PER_SIDE - 1));
    }
}

/// <summary>
/// 床の詳細編集を行うためのエディタウィンドウ。
/// </summary>
public class FloorDetailEditorWindow : DetailEditorWindowBase
{
    /// <summary>
    /// ウィンドウを開くための静的メソッド。
    /// </summary>
    public static void Open(SegmentData data, buildMaster editor)
    {
        FloorDetailEditorWindow window = GetWindow<FloorDetailEditorWindow>("Floor Detail");
        window.targetData = data;
        window.mainEditor = editor;
        float windowWidth = DETAIL_CELL_SIZE * SEGMENTS_PER_SIDE + 80;
        float windowHeight = DETAIL_CELL_SIZE * SEGMENTS_PER_SIDE + 220;
        window.minSize = new Vector2(windowWidth, windowHeight);
        window.Show();
    }
    // 床Prefabのリストをメインエディタから取得する。
    protected override List<PrefabColorData> GetPrefabList() => mainEditor.floorPrefabs;
    // 選択中の床Prefabのインデックスをメインエディタから取得する。
    protected override int GetSelectedPrefabIndex() => mainEditor.selectedFloorPrefab;

    /// <summary>
    /// 床エディタ固有のUI（タイトルやヘルプボックス）を描画する。
    /// </summary>
    protected override void DrawEditorSpecificControls()
    {
        EditorGUILayout.LabelField("4x4 床セグメントエディター", EditorStyles.boldLabel);
        EditorGUILayout.HelpBox("左クリック: 配置/解除 | Ctrl+ドラッグ: 矩形マージ", MessageType.Info);
    }
}

/// <summary>
/// 壁の詳細編集を行うためのエディタウィンドウ。ドア設置機能も持つ。
/// </summary>
public class WallDetailEditorWindow : DetailEditorWindowBase
{
    private bool isPlacingDoor = false; // ドア設置モード中かどうかのフラグ
    private Vector2Int doorSelectionStart; // ドア選択の開始セル
    private RectInt doorSelectionRect; // ドア選択の矩形範囲

    /// <summary>
    /// ウィンドウを開くための静的メソッド。
    /// </summary>
    public static void Open(SegmentData data, buildMaster editor)
    {
        WallDetailEditorWindow window = GetWindow<WallDetailEditorWindow>("Wall Detail");
        window.targetData = data;
        window.mainEditor = editor;
        float windowWidth = DETAIL_CELL_SIZE * SEGMENTS_PER_SIDE + 80;
        float windowHeight = DETAIL_CELL_SIZE * SEGMENTS_PER_SIDE + 260;
        window.minSize = new Vector2(windowWidth, windowHeight);
        window.Show();
    }
    // 壁Prefabのリストをメインエディタから取得する。
    protected override List<PrefabColorData> GetPrefabList() => mainEditor.wallPrefabs;
    // 選択中の壁Prefabのインデックスをメインエディタから取得する。
    protected override int GetSelectedPrefabIndex() => mainEditor.selectedWallPrefab;

    /// <summary>
    /// 壁エディタ固有のUI（タイトル、ヘルプ、選択中ドア情報）を描画する。
    /// </summary>
    protected override void DrawEditorSpecificControls()
    {
        EditorGUILayout.LabelField("4x4 壁セグメントエディター", EditorStyles.boldLabel);
        EditorGUILayout.HelpBox("壁の編集: 左クリックで配置/解除 | Ctrl+ドラッグで矩形マージ\nドアの設置: Shiftキーを押しながらドラッグで範囲指定", MessageType.Info);

        // 選択中のドアPrefab情報を表示
        EditorGUILayout.BeginVertical("box");
        EditorGUILayout.LabelField("選択中のドア Prefab", EditorStyles.miniBoldLabel);
        if (mainEditor.selectedDoorPrefab != -1)
        {
            var prefabData = mainEditor.doorPrefabs[mainEditor.selectedDoorPrefab];
            EditorGUILayout.BeginHorizontal();
            EditorGUI.DrawRect(GUILayoutUtility.GetRect(20, 20), prefabData.previewColor);
            EditorGUILayout.LabelField(prefabData.prefab != null ? prefabData.prefab.name : "(Prefabがありません)");
            EditorGUILayout.EndHorizontal();
        }
        else
        {
            EditorGUILayout.LabelField("なし (ドアは配置できません)");
        }
        EditorGUILayout.EndVertical();
        EditorGUILayout.Space();
    }

    /// <summary>
    /// グリッドコンテンツの描画。基底クラスの描画に加え、ドアの表示とドア設置の入力を処理する。
    /// </summary>
    protected override void DrawEditorContent(Event e, Rect gridArea)
    {
        base.DrawEditorContent(e, gridArea); // まず壁のグリッドを描画

        // 設置済みのドアを半透明で表示
        if (targetData.doorPrefabIndex != -1 && targetData.doorRect.width > 0)
        {
            Rect doorRectVisual = new Rect(gridArea.x + targetData.doorRect.x * DETAIL_CELL_SIZE, gridArea.y + targetData.doorRect.y * DETAIL_CELL_SIZE, targetData.doorRect.width * DETAIL_CELL_SIZE, targetData.doorRect.height * DETAIL_CELL_SIZE);
            Color doorColor = mainEditor.doorPrefabs[targetData.doorPrefabIndex].previewColor;
            doorColor.a = 0.8f;
            EditorGUI.DrawRect(doorRectVisual, doorColor);
            GUI.Label(doorRectVisual, "DOOR", new GUIStyle { alignment = TextAnchor.MiddleCenter, fontSize = 18, fontStyle = FontStyle.Bold, normal = { textColor = Color.white } });
        }

        // ドア設置モード中に選択範囲をハイライト
        if (isPlacingDoor)
        {
            Rect selectionVisual = new Rect(gridArea.x + doorSelectionRect.x * DETAIL_CELL_SIZE, gridArea.y + doorSelectionRect.y * DETAIL_CELL_SIZE, doorSelectionRect.width * DETAIL_CELL_SIZE, doorSelectionRect.height * DETAIL_CELL_SIZE);
            EditorGUI.DrawRect(selectionVisual, new Color(0.2f, 0.8f, 1.0f, 0.5f));
            Handles.color = new Color(0.2f, 0.8f, 1.0f, 0.9f);
            Handles.DrawWireCube(selectionVisual.center, selectionVisual.size);
            Handles.color = Color.white;
        }

        HandleDoorPlacementInput(e, gridArea); // ドア設置の入力処理
    }

    /// <summary>
    /// Shiftキーを押しながらのドラッグによるドア設置の入力を処理する。
    /// </summary>
    private void HandleDoorPlacementInput(Event e, Rect gridArea)
    {
        if (gridArea.Contains(e.mousePosition) && e.shift && mainEditor.selectedDoorPrefab != -1)
        {
            if (e.type == EventType.MouseDown && e.button == 0)
            {
                isPlacingDoor = true;
                doorSelectionStart = GetCellFromPosition(e.mousePosition, gridArea);
                e.Use();
            }
            else if (e.type == EventType.MouseDrag && isPlacingDoor)
            {
                Vector2Int currentPoint = GetCellFromPosition(e.mousePosition, gridArea);
                int x = Mathf.Min(doorSelectionStart.x, currentPoint.x);
                int y = Mathf.Min(doorSelectionStart.y, currentPoint.y);
                int width = Mathf.Abs(doorSelectionStart.x - currentPoint.x) + 1;
                int height = Mathf.Abs(doorSelectionStart.y - currentPoint.y) + 1;
                doorSelectionRect = new RectInt(x, y, width, height);
                Repaint();
                e.Use();
            }
        }

        // マウスボタンが離されたらドア設置を確定
        if (e.type == EventType.MouseUp && e.button == 0 && isPlacingDoor)
        {
            targetData.SetDoor(mainEditor.selectedDoorPrefab, doorSelectionRect);
            isPlacingDoor = false;
            Repaint();
            e.Use();
        }
    }

    /// <summary>
    /// ボタンの描画。「ドア設定をクリア」ボタンを追加する。
    /// </summary>
    protected override void DrawButtons()
    {
        EditorGUILayout.BeginHorizontal();
        GUILayout.FlexibleSpace();

        // ドアが設定されている場合のみ「クリア」ボタンを有効化
        GUI.enabled = targetData.doorPrefabIndex != -1;
        if (GUILayout.Button("ドア設定をクリア", GUILayout.Width(120), GUILayout.Height(30)))
        {
            targetData.ClearDoor();
            Repaint();
        }
        GUI.enabled = true;

        GUILayout.Space(10);

        if (GUILayout.Button("OK", GUILayout.Width(120), GUILayout.Height(30)))
        {
            targetData.isDetailed = targetData.IsActive();
            mainEditor.Repaint();
            Close();
        }
        GUILayout.FlexibleSpace();
        EditorGUILayout.EndHorizontal();
        GUILayout.Space(10);
    }
}

/// <summary>
/// メインのビルドマスターエディタウィンドウ。
/// グリッドレイアウトの作成、Prefabの管理、シーンへのオブジェクト生成を行う。
/// </summary>
public class buildMaster : EditorWindow
{
    // 各種のPrefabリスト
    public List<PrefabColorData> floorPrefabs = new List<PrefabColorData>();
    public List<PrefabColorData> wallPrefabs = new List<PrefabColorData>();
    public List<PrefabColorData> doorPrefabs = new List<PrefabColorData>();

    // グリッドの設定
    private int gridWidth = 5;
    private int gridHeight = 5;
    private float tileSize_real = 5f; // シーンに生成する際の物理的なタイルサイズ

    // 現在選択中のPrefabのインデックス
    public int selectedFloorPrefab = -1;
    public int selectedWallPrefab = -1;
    public int selectedDoorPrefab = -1;

    // グリッドの各タイルのデータ
    private SegmentData[] floorData;
    private SegmentData[,] verticalWallData;   // 垂直な壁（タイルの左右）
    private SegmentData[,] horizontalWallData; // 水平な壁（タイルの上下）

    // UI用の変数
    private Vector2 mainScroll;
    private bool showFloorPrefabList = true; // Prefabリストの折りたたみ状態
    private bool showWallPrefabList = true;
    private bool showDoorPrefabList = true;
    private DragMode dragMode = DragMode.None; // グリッドプレビュー上でのドラッグモード
    private enum DragMode { None, FloorPainting, FloorErasing, WallPainting, WallErasing, DoorPainting, FloorPasting, WallPasting }

    // コピー＆ペースト機能用の変数
    private SegmentData copiedSegmentData = null;
    private enum CopiedDataType { None, Floor, Wall }
    private CopiedDataType copiedDataType = CopiedDataType.None;

    // UI定数
    private const float PALETTE_ITEM_SIZE = 64f; // パレットのアイテムサイズ
    private const float GRID_CELL_UI_SIZE = 50f; // プレビューグリッドのセルサイズ
    private const float WALL_THICKNESS_UI = 12f; // プレビューグリッドの壁の太さ
    private int generationCount = 0; // 生成したオブジェクトのナンバリング用

    /// <summary>
    /// メニューからウィンドウを開く。
    /// </summary>
    [MenuItem("build Master/build Master")]
    public static void OpenWindow()
    {
        GetWindow<buildMaster>("build Master");
    }

    /// <summary>
    /// ウィンドウが有効になったときに呼ばれる。
    /// </summary>
    private void OnEnable()
    {
        InitializeGrid();
    }

    /// <summary>
    /// グリッドデータを現在のグリッドサイズで初期化（またはリセット）する。
    /// </summary>
    private void InitializeGrid()
    {
        floorData = new SegmentData[gridWidth * gridHeight];
        for (int i = 0; i < floorData.Length; i++)
        {
            floorData[i] = new SegmentData();
        }

        verticalWallData = new SegmentData[gridWidth + 1, gridHeight];
        horizontalWallData = new SegmentData[gridWidth, gridHeight + 1];

        for (int x = 0; x < gridWidth + 1; x++) for (int z = 0; z < gridHeight; z++) verticalWallData[x, z] = new SegmentData();
        for (int x = 0; x < gridWidth; x++) for (int z = 0; z < gridHeight + 1; z++) horizontalWallData[x, z] = new SegmentData();
    }

    /// <summary>
    /// メインウィンドウのUIを描画する。
    /// </summary>
    private void OnGUI()
    {
        mainScroll = EditorGUILayout.BeginScrollView(mainScroll);

        var sectionStyle = new GUIStyle("box") { padding = new RectOffset(10, 10, 10, 10), margin = new RectOffset(5, 5, 5, 5) };
        var headerStyle = new GUIStyle(EditorStyles.boldLabel) { fontSize = 14 };

        EditorGUILayout.BeginVertical(sectionStyle);
        EditorGUILayout.LabelField("基本設定", headerStyle);
        EditorGUILayout.Space(5);
        DrawSettingsUI();
        EditorGUILayout.EndVertical();

        EditorGUILayout.BeginVertical(sectionStyle);
        EditorGUILayout.LabelField("Prefab パレット", headerStyle);
        EditorGUILayout.Space(5);
        DrawPalettes();
        EditorGUILayout.EndVertical();

        EditorGUILayout.BeginVertical(sectionStyle);
        if (GUILayout.Button("シーンに設置", GUILayout.Height(40)))
        {
            GenerateSceneObjects();
        }
        EditorGUILayout.EndVertical();

        EditorGUILayout.BeginVertical(sectionStyle);
        EditorGUILayout.LabelField("レイアウトプレビュー", headerStyle);
        EditorGUILayout.Space(5);
        DrawPreviewHeader();
        DrawGridPreview();
        EditorGUILayout.EndVertical();

        EditorGUILayout.EndScrollView();
    }

    /// <summary>
    /// 各Prefabパレットを描画し、選択状態を管理する。
    /// </summary>
    private void DrawPalettes()
    {
        int originalFloor = selectedFloorPrefab, originalWall = selectedWallPrefab, originalDoor = selectedDoorPrefab;
        selectedFloorPrefab = DrawPrefabPalette("床 Prefabs", floorPrefabs, selectedFloorPrefab, ref showFloorPrefabList);
        selectedWallPrefab = DrawPrefabPalette("壁 Prefabs", wallPrefabs, selectedWallPrefab, ref showWallPrefabList);
        selectedDoorPrefab = DrawPrefabPalette("ドア Prefabs", doorPrefabs, selectedDoorPrefab, ref showDoorPrefabList);

        // いずれかのカテゴリでPrefabを選択したら、他のカテゴリの選択は解除する
        if (selectedFloorPrefab != originalFloor && selectedFloorPrefab != -1) { selectedWallPrefab = -1; selectedDoorPrefab = -1; }
        if (selectedWallPrefab != originalWall && selectedWallPrefab != -1) { selectedFloorPrefab = -1; selectedDoorPrefab = -1; }
        if (selectedDoorPrefab != originalDoor && selectedDoorPrefab != -1) { selectedFloorPrefab = -1; selectedWallPrefab = -1; }
    }

    /// <summary>
    /// 基本設定（グリッドサイズなど）のUIを描画する。
    /// </summary>
    private void DrawSettingsUI()
    {
        EditorGUI.BeginChangeCheck();
        gridWidth = EditorGUILayout.IntSlider("横の大きさ", gridWidth, 1, 50);
        gridHeight = EditorGUILayout.IntSlider("縦の大きさ", gridHeight, 1, 50);
        // グリッドサイズが変更されたら、グリッドデータを初期化
        if (EditorGUI.EndChangeCheck())
        {
            InitializeGrid();
        }
        tileSize_real = EditorGUILayout.FloatField("タイルの物理サイズ", tileSize_real);
    }

    /// <summary>
    /// グリッドプレビューの上部のヘッダー（ヘルプ、コピー情報）を描画する。
    /// </summary>
    private void DrawPreviewHeader()
    {
        EditorGUILayout.HelpBox("左クリック: 配置/解除 | 右クリック: 詳細編集\nAlt + 左クリック: コピー | Shift + 左クリック/ドラッグ: ペースト", MessageType.Info);

        // コピー中のデータがある場合、その情報を表示
        if (copiedSegmentData != null)
        {
            EditorGUILayout.BeginVertical("helpBox");
            string typeStr = "";
            Color color = Color.white;
            int prefabIndex = copiedSegmentData.segmentPrefabIndices[0];

            switch (copiedDataType)
            {
                case CopiedDataType.Floor:
                    typeStr = "床タイル";
                    if (prefabIndex >= 0 && prefabIndex < floorPrefabs.Count) color = floorPrefabs[prefabIndex].previewColor;
                    break;
                case CopiedDataType.Wall:
                    typeStr = "壁タイル";
                    if (prefabIndex >= 0 && prefabIndex < wallPrefabs.Count) color = wallPrefabs[prefabIndex].previewColor;
                    else if (copiedSegmentData.doorPrefabIndex >= 0 && copiedSegmentData.doorPrefabIndex < doorPrefabs.Count)
                        color = doorPrefabs[copiedSegmentData.doorPrefabIndex].previewColor;
                    break;
            }

            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField("コピー中:", GUILayout.Width(60));
            EditorGUI.DrawRect(GUILayoutUtility.GetRect(20, 20), copiedSegmentData.isDetailed ? Color.magenta : color);
            EditorGUILayout.LabelField(typeStr + (copiedSegmentData.isDetailed ? " (詳細設定)" : ""));
            GUILayout.FlexibleSpace();
            if (GUILayout.Button("クリア", GUILayout.Width(60)))
            {
                copiedSegmentData = null;
                copiedDataType = CopiedDataType.None;
            }
            EditorGUILayout.EndHorizontal();
            EditorGUILayout.EndVertical();
        }
        EditorGUILayout.Space(5);
    }

    /// <summary>
    /// 1つのPrefabパレット（リストと選択グリッド）を描画する。
    /// </summary>
    /// <returns>新しい選択Prefabインデックス。</returns>
    private int DrawPrefabPalette(string label, List<PrefabColorData> prefabs, int currentSelectedIndex, ref bool isFoldoutOpen)
    {
        int newSelectedIndex = currentSelectedIndex;
        var foldoutStyle = new GUIStyle(EditorStyles.foldoutHeader) { fontStyle = FontStyle.Bold };

        // 折りたたみ可能なヘッダー
        EditorGUILayout.BeginHorizontal();
        isFoldoutOpen = EditorGUILayout.Foldout(isFoldoutOpen, label, true, foldoutStyle);
        GUILayout.FlexibleSpace();
        // Prefab追加ボタン
        if (GUILayout.Button(EditorGUIUtility.IconContent("d_Toolbar Plus", "Prefabを追加"), GUI.skin.label, GUILayout.Width(20), GUILayout.Height(20)))
        {
            prefabs.Add(new PrefabColorData());
        }
        EditorGUILayout.EndHorizontal();

        // 折りたたみが開いている場合、Prefabの設定リストを表示
        if (isFoldoutOpen)
        {
            EditorGUILayout.BeginVertical("box");
            for (int i = 0; i < prefabs.Count; i++)
            {
                EditorGUILayout.BeginHorizontal();
                prefabs[i].prefab = (GameObject)EditorGUILayout.ObjectField(prefabs[i].prefab, typeof(GameObject), false, GUILayout.MinWidth(100));
                prefabs[i].previewColor = EditorGUILayout.ColorField(prefabs[i].previewColor, GUILayout.Width(70));

                // 削除ボタン
                if (GUILayout.Button(EditorGUIUtility.IconContent("d_Toolbar Minus", "削除"), GUILayout.Width(25)))
                {
                    prefabs.RemoveAt(i);
                    // 選択インデックスを調整
                    if (currentSelectedIndex == i) newSelectedIndex = -1;
                    else if (currentSelectedIndex > i) newSelectedIndex--;
                    GUI.FocusControl(null);
                    i--; // ループインデックスを調整
                }
                EditorGUILayout.EndHorizontal();
            }
            EditorGUILayout.EndVertical();
        }
        EditorGUILayout.Space(5);

        // Prefab選択用のグリッドUI
        if (prefabs.Count > 0)
        {
            // ウィンドウ幅に合わせて1行に表示するアイテム数を計算
            int itemsPerRow = Mathf.FloorToInt((EditorGUIUtility.currentViewWidth - 40) / (PALETTE_ITEM_SIZE + 5f));
            if (itemsPerRow < 1) itemsPerRow = 1;

            for (int i = 0; i < prefabs.Count; i++)
            {
                if (i % itemsPerRow == 0)
                {
                    if (i > 0) EditorGUILayout.EndHorizontal();
                    EditorGUILayout.BeginHorizontal();
                }
                PrefabColorData item = prefabs[i];
                if (item.prefab == null) continue;

                Texture2D preview = AssetPreview.GetAssetPreview(item.prefab);
                GUIContent buttonContent = new GUIContent(preview, item.prefab.name);
                bool isSelected = (i == currentSelectedIndex);

                Color originalBgColor = GUI.backgroundColor;
                GUI.backgroundColor = item.previewColor; // 背景色をプレビューカラーに設定

                Rect buttonRect = GUILayoutUtility.GetRect(PALETTE_ITEM_SIZE, PALETTE_ITEM_SIZE);
                // 選択中のアイテムには枠線を表示
                if (isSelected)
                {
                    Rect outlineRect = new Rect(buttonRect.x - 3, buttonRect.y - 3, buttonRect.width + 6, buttonRect.height + 6);
                    GUI.backgroundColor = Color.cyan;
                    GUI.Box(outlineRect, GUIContent.none, "selectionRect");
                    GUI.backgroundColor = item.previewColor;
                }

                if (GUI.Button(buttonRect, buttonContent))
                {
                    // ボタンクリックで選択/選択解除
                    newSelectedIndex = isSelected ? -1 : i;
                    GUI.FocusControl(null); // フォーカスを外す
                }
                GUI.backgroundColor = originalBgColor;
            }
            EditorGUILayout.EndHorizontal();
        }
        GUI.color = Color.white;
        EditorGUILayout.Space(10);
        return newSelectedIndex;
    }

    /// <summary>
    /// メインのグリッドプレビューを描画する。
    /// </summary>
    private void DrawGridPreview()
    {
        Event e = Event.current;
        // グリッド全体の描画領域を計算
        Rect gridArea = GUILayoutUtility.GetRect((GRID_CELL_UI_SIZE + WALL_THICKNESS_UI) * gridWidth + WALL_THICKNESS_UI, (GRID_CELL_UI_SIZE + WALL_THICKNESS_UI) * gridHeight + WALL_THICKNESS_UI);
        gridArea.x = (EditorGUIUtility.currentViewWidth - gridArea.width) / 2; // 中央揃え

        // マウスが離れたらドラッグモードを解除
        if (e.type == EventType.MouseUp) dragMode = DragMode.None;

        // 全てのタイルをループ
        for (int x = 0; x < gridWidth; x++)
        {
            for (int z = 0; z < gridHeight; z++)
            {
                // UI上の座標を計算（Y軸が反転していることに注意）
                float U_xPos = gridArea.x + x * (GRID_CELL_UI_SIZE + WALL_THICKNESS_UI) + WALL_THICKNESS_UI;
                float U_yPos = gridArea.y + (gridHeight - 1 - z) * (GRID_CELL_UI_SIZE + WALL_THICKNESS_UI) + WALL_THICKNESS_UI;
                int tileIndex = z * gridWidth + x;

                // 床タイルを描画
                Rect tileRect = new Rect(U_xPos, U_yPos, GRID_CELL_UI_SIZE, GRID_CELL_UI_SIZE);
                DrawFloorTile(e, tileRect, floorData[tileIndex]);

                // 周囲の壁タイルを描画
                Rect topWallRect = new Rect(tileRect.x, tileRect.y - WALL_THICKNESS_UI, GRID_CELL_UI_SIZE, WALL_THICKNESS_UI);
                DrawWallTile(e, topWallRect, horizontalWallData[x, z + 1]);

                Rect leftWallRect = new Rect(tileRect.x - WALL_THICKNESS_UI, tileRect.y, WALL_THICKNESS_UI, GRID_CELL_UI_SIZE);
                DrawWallTile(e, leftWallRect, verticalWallData[x, z]);

                // グリッドの右端と下端の壁を描画
                if (x == gridWidth - 1)
                {
                    Rect rightWallRect = new Rect(tileRect.x + GRID_CELL_UI_SIZE, tileRect.y, WALL_THICKNESS_UI, GRID_CELL_UI_SIZE);
                    DrawWallTile(e, rightWallRect, verticalWallData[x + 1, z]);
                }
                if (z == 0)
                {
                    Rect bottomWallRect = new Rect(tileRect.x, tileRect.y + GRID_CELL_UI_SIZE, GRID_CELL_UI_SIZE, WALL_THICKNESS_UI);
                    DrawWallTile(e, bottomWallRect, horizontalWallData[x, z]);
                }
            }
        }
    }

    /// <summary>
    /// 1つの床タイルをプレビューに描画する。
    /// </summary>
    private void DrawFloorTile(Event e, Rect rect, SegmentData data)
    {
        Color color = new Color(0.3f, 0.3f, 0.3f); // デフォルト（空）の色
        if (data.isDetailed)
        {
            // 詳細編集されている場合は虹色に点滅させて示す
            float hue = (float)(EditorApplication.timeSinceStartup * 0.7f % 1.0);
            color = Color.HSVToRGB(hue, 0.6f, 0.8f);
            GUI.Label(rect, EditorGUIUtility.IconContent("d_CustomTool"), new GUIStyle { alignment = TextAnchor.MiddleCenter });
            Repaint();
        }
        else if (data.IsActive())
        {
            // 通常のPrefabが設定されている場合
            int prefabIndex = data.segmentPrefabIndices[0];
            if (prefabIndex >= 0 && prefabIndex < floorPrefabs.Count && floorPrefabs[prefabIndex].prefab != null)
                color = floorPrefabs[prefabIndex].previewColor;
            else
                color = Color.magenta; // 不正なデータはマゼンタで表示
        }
        EditorGUI.DrawRect(rect, color);
        HandleFloorMouseInput(e, rect, data); // マウス入力処理
    }

    /// <summary>
    /// 1つの壁タイルをプレビューに描画する。
    /// </summary>
    private void DrawWallTile(Event e, Rect wallRect, SegmentData wallData)
    {
        Color color = new Color(0.4f, 0.4f, 0.4f); // デフォルト（空）の色
        if (wallData.doorPrefabIndex != -1)
        {
            // ドアが設定されている場合
            if (wallData.doorPrefabIndex >= 0 && wallData.doorPrefabIndex < doorPrefabs.Count)
            {
                color = doorPrefabs[wallData.doorPrefabIndex].previewColor;
                GUI.Label(wallRect, EditorGUIUtility.IconContent("d_SceneViewOrtho"), new GUIStyle { alignment = TextAnchor.MiddleCenter });
            }
        }
        else if (wallData.isDetailed)
        {
            // 詳細編集されている場合は虹色に点滅
            float hue = (float)(EditorApplication.timeSinceStartup * 0.7f % 1.0);
            color = Color.HSVToRGB(hue, 0.7f, 0.9f);
            GUI.Label(wallRect, EditorGUIUtility.IconContent("d_CustomTool"), new GUIStyle { alignment = TextAnchor.MiddleCenter });
            Repaint();
        }
        else if (wallData.IsActive())
        {
            // 通常のPrefabが設定されている場合
            int prefabIndex = wallData.segmentPrefabIndices[0];
            if (prefabIndex >= 0 && prefabIndex < wallPrefabs.Count && wallPrefabs[prefabIndex].prefab != null)
                color = wallPrefabs[prefabIndex].previewColor;
            else
                color = Color.magenta; // 不正なデータ
        }
        EditorGUI.DrawRect(wallRect, color);
        HandleWallMouseInput(e, wallRect, wallData); // マウス入力処理
    }

    /// <summary>
    /// 床タイルに対するマウス入力を処理する（ペイント、消去、コピー、ペースト、詳細編集）。
    /// </summary>
    private void HandleFloorMouseInput(Event e, Rect rect, SegmentData data)
    {
        if (rect.Contains(e.mousePosition))
        {
            if (e.type == EventType.MouseDown)
            {
                // Alt + 左クリック: コピー
                if (e.alt && e.button == 0)
                {
                    copiedSegmentData = new SegmentData();
                    Array.Copy(data.segmentPrefabIndices, copiedSegmentData.segmentPrefabIndices, data.segmentPrefabIndices.Length);
                    copiedSegmentData.isDetailed = data.isDetailed;
                    copiedDataType = CopiedDataType.Floor;
                    Debug.Log("床タイルをコピーしました。");
                    e.Use();
                }
                // Shift + 左クリック: ペースト
                else if (e.shift && e.button == 0 && copiedSegmentData != null && copiedDataType == CopiedDataType.Floor)
                {
                    dragMode = DragMode.FloorPasting;
                    Array.Copy(copiedSegmentData.segmentPrefabIndices, data.segmentPrefabIndices, copiedSegmentData.segmentPrefabIndices.Length);
                    data.isDetailed = copiedSegmentData.isDetailed;
                    e.Use(); Repaint();
                }
                // 右クリック: 詳細編集ウィンドウを開く
                else if (e.button == 1)
                {
                    FloorDetailEditorWindow.Open(data, this);
                    e.Use();
                }
                // 左クリック: ペイント/消去
                else if (e.button == 0 && dragMode == DragMode.None)
                {
                    if (selectedFloorPrefab != -1)
                    {
                        if (data.IsActive()) // 既に塗られていれば消去
                        {
                            dragMode = DragMode.FloorErasing;
                            data.FillAllSegments(-1);
                        }
                        else // 塗られていなければペイント
                        {
                            dragMode = DragMode.FloorPainting;
                            data.FillAllSegments(selectedFloorPrefab);
                        }
                    }
                    else if (selectedWallPrefab == -1 && selectedDoorPrefab == -1) // 消しゴムモード
                    {
                        dragMode = DragMode.FloorErasing;
                        data.FillAllSegments(-1);
                    }
                    e.Use(); Repaint();
                }
            }
            // ドラッグ中の処理
            else if (e.type == EventType.MouseDrag && e.button == 0)
            {
                if (dragMode == DragMode.FloorPasting && copiedSegmentData != null && copiedDataType == CopiedDataType.Floor)
                {
                    Array.Copy(copiedSegmentData.segmentPrefabIndices, data.segmentPrefabIndices, copiedSegmentData.segmentPrefabIndices.Length);
                    data.isDetailed = copiedSegmentData.isDetailed;
                    e.Use(); Repaint();
                }
                else if (dragMode == DragMode.FloorPainting && !data.IsActive())
                {
                    data.FillAllSegments(selectedFloorPrefab);
                    e.Use(); Repaint();
                }
                else if (dragMode == DragMode.FloorErasing && data.IsActive())
                {
                    data.FillAllSegments(-1);
                    e.Use(); Repaint();
                }
            }
        }
    }

    /// <summary>
    /// 壁タイルに対するマウス入力を処理する（ペイント、消去、コピー、ペースト、詳細編集）。
    /// </summary>
    private void HandleWallMouseInput(Event e, Rect rect, SegmentData wallData)
    {
        if (rect.Contains(e.mousePosition))
        {
            if (e.type == EventType.MouseDown)
            {
                // Alt + 左クリック: コピー
                if (e.alt && e.button == 0)
                {
                    copiedSegmentData = new SegmentData();
                    Array.Copy(wallData.segmentPrefabIndices, copiedSegmentData.segmentPrefabIndices, wallData.segmentPrefabIndices.Length);
                    copiedSegmentData.isDetailed = wallData.isDetailed;
                    copiedSegmentData.doorPrefabIndex = wallData.doorPrefabIndex;
                    copiedSegmentData.doorRect = wallData.doorRect;
                    copiedDataType = CopiedDataType.Wall;
                    Debug.Log("壁タイルをコピーしました。");
                    e.Use();
                }
                // Shift + 左クリック: ペースト
                else if (e.shift && e.button == 0 && copiedSegmentData != null && copiedDataType == CopiedDataType.Wall)
                {
                    dragMode = DragMode.WallPasting;
                    Array.Copy(copiedSegmentData.segmentPrefabIndices, wallData.segmentPrefabIndices, copiedSegmentData.segmentPrefabIndices.Length);
                    wallData.isDetailed = copiedSegmentData.isDetailed;
                    wallData.doorPrefabIndex = copiedSegmentData.doorPrefabIndex;
                    wallData.doorRect = copiedSegmentData.doorRect;
                    e.Use(); Repaint();
                }
                // 右クリック: 詳細編集
                else if (e.button == 1)
                {
                    WallDetailEditorWindow.Open(wallData, this);
                    e.Use();
                }
                // 左クリック: ペイント/消去
                else if (e.button == 0 && dragMode == DragMode.None)
                {
                    // ドアが選択されている場合
                    if (selectedDoorPrefab != -1)
                    {
                        dragMode = DragMode.DoorPainting;
                        // 同じドアなら消去、違うドアなら上書き
                        if (wallData.doorPrefabIndex == selectedDoorPrefab)
                        {
                            wallData.FillAllSegments(-1);
                        }
                        else
                        {
                            wallData.FillAllSegments(-1); // まず壁をクリア
                            wallData.SetDoor(selectedDoorPrefab, new RectInt(0, 0, 4, 4)); // ドアを設置
                        }
                    }
                    // 壁が選択されている場合
                    else if (selectedWallPrefab != -1)
                    {
                        if (wallData.IsActive()) // 塗られていれば消去
                        {
                            dragMode = DragMode.WallErasing;
                            wallData.FillAllSegments(-1);
                        }
                        else // 塗られていなければペイント
                        {
                            dragMode = DragMode.WallPainting;
                            wallData.FillAllSegments(selectedWallPrefab);
                        }
                    }
                    // 消しゴムモード
                    else
                    {
                        dragMode = DragMode.WallErasing;
                        wallData.FillAllSegments(-1);
                    }
                    e.Use(); Repaint();
                }
            }
            // ドラッグ中の処理
            else if (e.type == EventType.MouseDrag && e.button == 0)
            {
                if (dragMode == DragMode.WallPasting && copiedSegmentData != null && copiedDataType == CopiedDataType.Wall)
                {
                    Array.Copy(copiedSegmentData.segmentPrefabIndices, wallData.segmentPrefabIndices, copiedSegmentData.segmentPrefabIndices.Length);
                    wallData.isDetailed = copiedSegmentData.isDetailed;
                    wallData.doorPrefabIndex = copiedSegmentData.doorPrefabIndex;
                    wallData.doorRect = copiedSegmentData.doorRect;
                    e.Use(); Repaint();
                }
                else if (dragMode == DragMode.WallPainting && !wallData.IsActive())
                {
                    wallData.FillAllSegments(selectedWallPrefab);
                    e.Use(); Repaint();
                }
                else if (dragMode == DragMode.WallErasing && wallData.IsActive())
                {
                    wallData.FillAllSegments(-1);
                    e.Use(); Repaint();
                }
            }
        }
    }

    /// <summary>
    /// 現在のグリッドデータに基づいて、シーンにGameObjectを生成する。
    /// </summary>
    private void GenerateSceneObjects()
    {
        GameObject parentRoom = new GameObject("Room_" + generationCount);
        Undo.RegisterCreatedObjectUndo(parentRoom, "Create Room");
        GenerateFloor(parentRoom.transform);
        GenerateWalls(parentRoom.transform);
        generationCount++;
    }

    /// <summary>
    /// 床オブジェクトを生成する。
    /// </summary>
    private void GenerateFloor(Transform parentRoom)
    {
        if (floorPrefabs.Count == 0) return;
        GameObject parentFloor = new GameObject("Floor");
        parentFloor.transform.SetParent(parentRoom);

        int segmentsPerSide = 4;
        float segmentSize = tileSize_real / segmentsPerSide;

        for (int i = 0; i < floorData.Length; i++)
        {
            SegmentData data = floorData[i];
            if (!data.IsActive()) continue;

            int tileX = i % gridWidth;
            int tileZ = i / gridWidth;

            GameObject floorTileParent = new GameObject($"Floor_Tile_{tileX}_{tileZ}");
            floorTileParent.transform.SetParent(parentFloor.transform);
            floorTileParent.transform.position = new Vector3(tileX * tileSize_real, 0, tileZ * tileSize_real);

            bool[] generated = new bool[data.segmentPrefabIndices.Length];
            for (int segIdx = 0; segIdx < data.segmentPrefabIndices.Length; segIdx++)
            {
                if (generated[segIdx]) continue;

                // このセグメントが属するグループの親を取得
                int originIndex = GetMergeOriginIndex(data, segIdx);
                if (originIndex != segIdx) continue; // 親でなければ処理しない

                int prefabIdx = data.segmentPrefabIndices[originIndex];
                if (prefabIdx < 0) // 空のグループはスキップ
                {
                    foreach (var cellPos in GetAllCellsInGroup(data, originIndex))
                    {
                        generated[cellPos.y * segmentsPerSide + cellPos.x] = true;
                    }
                    continue;
                }

                GameObject prefab = floorPrefabs[prefabIdx].prefab;
                List<Vector2Int> groupCells = GetAllCellsInGroup(data, originIndex);
                Bounds prefabBounds = GetPrefabBounds(prefab);

                if (groupCells.Count > 0)
                {
                    // グループのバウンディングボックスを計算
                    int minX = groupCells.Min(p => p.x);
                    int minY = groupCells.Min(p => p.y);
                    int maxX = groupCells.Max(p => p.x);
                    int maxY = groupCells.Max(p => p.y);

                    int groupWidthInSegments = maxX - minX + 1;
                    int groupHeightInSegments = maxY - minY + 1;

                    float groupWidth = groupWidthInSegments * segmentSize;
                    float groupHeight = groupHeightInSegments * segmentSize;

                    // グループの中心座標を計算
                    float centerX = minX * segmentSize + groupWidth / 2.0f;
                    float centerZ = (segmentsPerSide - 1 - maxY) * segmentSize + groupHeight / 2.0f;

                    GameObject mergedObject = PrefabUtility.InstantiatePrefab(prefab) as GameObject;
                    mergedObject.transform.SetParent(floorTileParent.transform, false);

                    // PrefabのBoundsの中心とずらして配置
                    Vector3 position = new Vector3(centerX, 0, centerZ);
                    mergedObject.transform.localPosition = position - prefabBounds.center;

                    // Prefabをグループのサイズに合わせてスケーリング
                    Vector3 baseSize = prefabBounds.size;
                    if (Mathf.Approximately(baseSize.x, 0)) baseSize.x = 1f;
                    if (Mathf.Approximately(baseSize.y, 0)) baseSize.y = 1f; // 床なのでYは薄くする
                    if (Mathf.Approximately(baseSize.z, 0)) baseSize.z = 1f;

                    Vector3 originalScale = prefab.transform.localScale;
                    float scaleX = groupWidth / baseSize.x;
                    float scaleY = (baseSize.y > 0.001f) ? (0.01f / baseSize.y) : 1f;
                    float scaleZ = groupHeight / baseSize.z;
                    mergedObject.transform.localScale = new Vector3(originalScale.x * scaleX, originalScale.y * scaleY, originalScale.z * scaleZ);

                    Undo.RegisterCreatedObjectUndo(mergedObject, "Create Merged Floor");
                }

                foreach (var cellPos in groupCells)
                    generated[cellPos.y * segmentsPerSide + cellPos.x] = true;
            }
        }
    }

    /// <summary>
    /// 壁とドアのオブジェクトを生成する。
    /// </summary>
    private void GenerateWalls(Transform parentRoom)
    {
        if (wallPrefabs.Count == 0 && doorPrefabs.Count == 0) return;
        GameObject parentWall = new GameObject("Walls");
        parentWall.transform.SetParent(parentRoom);

        float wallThickness = 0.1f;
        int segmentsPerSide = 4;
        float segmentSize = tileSize_real / segmentsPerSide;

        // 垂直な壁の生成
        for (int x = 0; x < gridWidth + 1; x++)
        {
            for (int z = 0; z < gridHeight; z++)
            {
                SegmentData data = verticalWallData[x, z];
                if (!data.IsActive()) continue;

                GameObject wallTileParent = new GameObject($"Wall_V_{x}_{z}");
                wallTileParent.transform.SetParent(parentWall.transform);
                float wallCenterX = x * tileSize_real;
                float wallCenterZ = z * tileSize_real + tileSize_real / 2.0f;
                wallTileParent.transform.position = new Vector3(wallCenterX, 0, wallCenterZ);

                // ドアの生成
                if (data.doorPrefabIndex != -1)
                {
                    if (data.doorPrefabIndex < doorPrefabs.Count && doorPrefabs[data.doorPrefabIndex].prefab != null)
                    {
                        GameObject doorPrefab = doorPrefabs[data.doorPrefabIndex].prefab;
                        GameObject door = PrefabUtility.InstantiatePrefab(doorPrefab) as GameObject;

                        Bounds bounds = GetPrefabBounds(doorPrefab);
                        if (bounds.size.magnitude < 0.001f) { DestroyImmediate(door); continue; }

                        // ドアの矩形が不正ならタイル全体とする
                        RectInt doorRect = data.doorRect.width <= 0 ? new RectInt(0, 0, segmentsPerSide, segmentsPerSide) : data.doorRect;

                        float doorHeight = doorRect.height * segmentSize;
                        float doorWidth = doorRect.width * segmentSize;

                        // ドアのサイズをスケーリング
                        Vector3 localScale = door.transform.localScale;
                        door.transform.localScale = new Vector3(localScale.x * (doorWidth / bounds.size.x), localScale.y * (doorHeight / bounds.size.y), localScale.z);

                        door.transform.SetParent(wallTileParent.transform, false);
                        door.transform.localRotation = Quaternion.Euler(0, 90, 0); // 垂直な壁なのでY90度回転

                        Bounds scaledBounds = GetPrefabBounds(door); // スケール後のBoundsを取得

                        // ドアの位置を計算
                        int invertedY = (segmentsPerSide - 1) - (doorRect.y + doorRect.height - 1);
                        float bottomEdgeY = invertedY * segmentSize;
                        float leftEdgeZ = (doorRect.x * segmentSize) - (tileSize_real / 2.0f);

                        door.transform.localPosition = new Vector3(0 - scaledBounds.center.x, bottomEdgeY - scaledBounds.min.y, leftEdgeZ + doorWidth / 2f - scaledBounds.center.z);

                        Undo.RegisterCreatedObjectUndo(door, "Create Door");
                    }
                }

                // 壁セグメントの生成
                bool[] generated = new bool[data.segmentPrefabIndices.Length];
                for (int i = 0; i < data.segmentPrefabIndices.Length; i++)
                {
                    if (generated[i]) continue;

                    int originIndex = GetMergeOriginIndex(data, i);
                    if (originIndex != i) continue;

                    int prefabIdx = data.segmentPrefabIndices[originIndex];
                    if (prefabIdx < 0)
                    {
                        foreach (var cellPos in GetAllCellsInGroup(data, originIndex)) generated[cellPos.y * segmentsPerSide + cellPos.x] = true;
                        continue;
                    }

                    GameObject prefab = wallPrefabs[prefabIdx].prefab;
                    List<Vector2Int> groupCells = GetAllCellsInGroup(data, originIndex);
                    Bounds prefabBounds = GetPrefabBounds(prefab);

                    if (groupCells.Count > 0)
                    {
                        int minCellX = groupCells.Min(p => p.x);
                        int minCellY = groupCells.Min(p => p.y);
                        int maxCellX = groupCells.Max(p => p.x);
                        int maxCellY = groupCells.Max(p => p.y);

                        int groupWidthInSegments = maxCellX - minCellX + 1; // 壁のZ軸方向の長さ
                        int groupHeightInSegments = maxCellY - minCellY + 1; // 壁のY軸方向の高さ

                        float groupWidth = groupWidthInSegments * segmentSize;
                        float groupHeight = groupHeightInSegments * segmentSize;

                        // 位置計算
                        int invertedMinY = (segmentsPerSide - 1) - maxCellY;
                        float centerY = invertedMinY * segmentSize + groupHeight / 2.0f;
                        float centerZ = minCellX * segmentSize - tileSize_real / 2.0f + groupWidth / 2.0f;

                        GameObject mergedWall = PrefabUtility.InstantiatePrefab(prefab) as GameObject;
                        mergedWall.transform.SetParent(wallTileParent.transform, false);

                        Vector3 position = new Vector3(0, centerY, centerZ);
                        mergedWall.transform.localPosition = position - prefabBounds.center;

                        // スケール計算
                        Vector3 baseSize = prefabBounds.size;
                        if (Mathf.Approximately(baseSize.x, 0)) baseSize.x = 1f;
                        if (Mathf.Approximately(baseSize.y, 0)) baseSize.y = 1f;
                        if (Mathf.Approximately(baseSize.z, 0)) baseSize.z = 1f;

                        Vector3 originalScale = prefab.transform.localScale;
                        float scaleX = wallThickness / baseSize.x;
                        float scaleY = groupHeight / baseSize.y;
                        float scaleZ = groupWidth / baseSize.z;
                        mergedWall.transform.localScale = new Vector3(originalScale.x * scaleX, originalScale.y * scaleY, originalScale.z * scaleZ);

                        Undo.RegisterCreatedObjectUndo(mergedWall, "Create Merged Vertical Wall");
                    }

                    foreach (var cellPos in groupCells) generated[cellPos.y * segmentsPerSide + cellPos.x] = true;
                }
            }
        }

        // 水平な壁の生成（垂直な壁とほぼ同様のロジック）
        for (int x = 0; x < gridWidth; x++)
        {
            for (int z = 0; z < gridHeight + 1; z++)
            {
                SegmentData data = horizontalWallData[x, z];
                if (!data.IsActive()) continue;

                GameObject wallTileParent = new GameObject($"Wall_H_{x}_{z}");
                wallTileParent.transform.SetParent(parentWall.transform);
                float wallCenterX = x * tileSize_real + tileSize_real / 2.0f;
                float wallCenterZ = z * tileSize_real;
                wallTileParent.transform.position = new Vector3(wallCenterX, 0, wallCenterZ);

                // ドアの生成
                if (data.doorPrefabIndex != -1)
                {
                    if (data.doorPrefabIndex < doorPrefabs.Count && doorPrefabs[data.doorPrefabIndex].prefab != null)
                    {
                        GameObject doorPrefab = doorPrefabs[data.doorPrefabIndex].prefab;
                        GameObject door = PrefabUtility.InstantiatePrefab(doorPrefab) as GameObject;

                        Bounds bounds = GetPrefabBounds(doorPrefab);
                        if (bounds.size.magnitude < 0.001f) { DestroyImmediate(door); continue; }

                        RectInt doorRect = data.doorRect.width <= 0 ? new RectInt(0, 0, segmentsPerSide, segmentsPerSide) : data.doorRect;

                        float doorHeight = doorRect.height * segmentSize;
                        float doorWidth = doorRect.width * segmentSize;

                        Vector3 localScale = door.transform.localScale;
                        door.transform.localScale = new Vector3(localScale.x * (doorWidth / bounds.size.x), localScale.y * (doorHeight / bounds.size.y), localScale.z);

                        door.transform.SetParent(wallTileParent.transform, false);
                        door.transform.localRotation = Quaternion.identity; // 水平な壁は回転なし

                        Bounds scaledBounds = GetPrefabBounds(door);

                        int invertedY = (segmentsPerSide - 1) - (doorRect.y + doorRect.height - 1);
                        float bottomEdgeY = invertedY * segmentSize;
                        float leftEdgeX = (doorRect.x * segmentSize) - (tileSize_real / 2.0f);

                        door.transform.localPosition = new Vector3(leftEdgeX + doorWidth / 2f - scaledBounds.center.x, bottomEdgeY - scaledBounds.min.y, 0 - scaledBounds.center.z);

                        Undo.RegisterCreatedObjectUndo(door, "Create Door");
                    }
                }

                // 壁セグメントの生成
                bool[] generated = new bool[data.segmentPrefabIndices.Length];
                for (int i = 0; i < data.segmentPrefabIndices.Length; i++)
                {
                    if (generated[i]) continue;

                    int originIndex = GetMergeOriginIndex(data, i);
                    if (originIndex != i) continue;

                    int prefabIdx = data.segmentPrefabIndices[originIndex];
                    if (prefabIdx < 0)
                    {
                        foreach (var cellPos in GetAllCellsInGroup(data, originIndex)) generated[cellPos.y * segmentsPerSide + cellPos.x] = true;
                        continue;
                    }

                    GameObject prefab = wallPrefabs[prefabIdx].prefab;
                    List<Vector2Int> groupCells = GetAllCellsInGroup(data, originIndex);
                    Bounds prefabBounds = GetPrefabBounds(prefab);

                    if (groupCells.Count > 0)
                    {
                        int minCellX = groupCells.Min(p => p.x);
                        int minCellY = groupCells.Min(p => p.y);
                        int maxCellX = groupCells.Max(p => p.x);
                        int maxCellY = groupCells.Max(p => p.y);

                        int groupWidthInSegments = maxCellX - minCellX + 1; // 壁のX軸方向の長さ
                        int groupHeightInSegments = maxCellY - minCellY + 1; // 壁のY軸方向の高さ

                        float groupWidth = groupWidthInSegments * segmentSize;
                        float groupHeight = groupHeightInSegments * segmentSize;

                        float centerX = minCellX * segmentSize - tileSize_real / 2.0f + groupWidth / 2.0f;
                        int invertedMinY = (segmentsPerSide - 1) - maxCellY;
                        float centerY = invertedMinY * segmentSize + groupHeight / 2.0f;

                        GameObject mergedWall = PrefabUtility.InstantiatePrefab(prefab) as GameObject;
                        mergedWall.transform.SetParent(wallTileParent.transform, false);

                        Vector3 position = new Vector3(centerX, centerY, 0);
                        mergedWall.transform.localPosition = position - prefabBounds.center;

                        Vector3 baseSize = prefabBounds.size;
                        if (Mathf.Approximately(baseSize.x, 0)) baseSize.x = 1f;
                        if (Mathf.Approximately(baseSize.y, 0)) baseSize.y = 1f;
                        if (Mathf.Approximately(baseSize.z, 0)) baseSize.z = 1f;

                        Vector3 originalScale = prefab.transform.localScale;
                        float scaleX = groupWidth / baseSize.x;
                        float scaleY = groupHeight / baseSize.y;
                        float scaleZ = wallThickness / baseSize.z;
                        mergedWall.transform.localScale = new Vector3(originalScale.x * scaleX, originalScale.y * scaleY, originalScale.z * scaleZ);

                        Undo.RegisterCreatedObjectUndo(mergedWall, "Create Merged Horizontal Wall");
                    }

                    foreach (var cellPos in groupCells) generated[cellPos.y * segmentsPerSide + cellPos.x] = true;
                }
            }
        }
    }

    /// <summary>
    /// 指定されたSegmentData内のセルの親インデックスを取得するヘルパーメソッド。
    /// </summary>
    private int GetMergeOriginIndex(SegmentData data, int index)
    {
        if (index < 0 || index >= data.segmentPrefabIndices.Length) return -1;
        if (data.segmentPrefabIndices[index] < -1)
        {
            return -(data.segmentPrefabIndices[index] + 2);
        }
        return index;
    }

    /// <summary>
    /// 指定されたSegmentData内のマージグループの全セル座標を取得するヘルパーメソッド。
    /// </summary>
    private List<Vector2Int> GetAllCellsInGroup(SegmentData data, int originIndex)
    {
        int segmentsPerSide = 4;
        var groupCells = new List<Vector2Int>();
        if (originIndex < 0 || originIndex >= data.segmentPrefabIndices.Length) return groupCells;

        groupCells.Add(new Vector2Int(originIndex % segmentsPerSide, originIndex / segmentsPerSide));
        int searchPointer = -(originIndex + 2);

        for (int i = 0; i < data.segmentPrefabIndices.Length; i++)
        {
            if (data.segmentPrefabIndices[i] == searchPointer)
            {
                groupCells.Add(new Vector2Int(i % segmentsPerSide, i / segmentsPerSide));
            }
        }
        return groupCells;
    }

    /// <summary>
    /// Prefabのレンダラーをすべて含むバウンディングボックスを取得する。
    /// 一時的にインスタンスを生成して計算し、すぐに破棄する。
    /// </summary>
    /// <returns>ローカル座標系でのBounds。</returns>
    private Bounds GetPrefabBounds(GameObject prefab)
    {
        GameObject tempInstance = Instantiate(prefab); // 一時的にインスタンス化

        Renderer[] renderers = tempInstance.GetComponentsInChildren<Renderer>();
        if (renderers.Length == 0)
        {
            DestroyImmediate(tempInstance);
            return new Bounds(Vector3.zero, Vector3.zero);
        }

        Bounds bounds = new Bounds();
        bool first = true;
        foreach (var r in renderers)
        {
            if (first)
            {
                bounds = r.bounds;
                first = false;
            }
            else
            {
                bounds.Encapsulate(r.bounds); // 全てのレンダラーを包含するように拡張
            }
        }

        // ワールド座標のBoundsから、インスタンスの座標を引いてローカルなBoundsに変換
        Vector3 size = bounds.size;
        Vector3 center = bounds.center - tempInstance.transform.position;

        DestroyImmediate(tempInstance); // 一時オブジェクトを破棄

        return new Bounds(center, size);
    }
}
