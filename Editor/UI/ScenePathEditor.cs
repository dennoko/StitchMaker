using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace Dennokoworks.StitchMaker
{
    /// <summary>シーンビューでのパス編集（入力処理とギズモ描画）。座標はすべてメッシュ空間で扱う。</summary>
    public static class ScenePathEditor
    {
        const float NodePickRadius = 10f;
        const float LinePickRadius = 6f;

        enum DragTarget
        {
            None,
            Node,
            HandleIn,
            HandleOut,
        }

        static readonly int ControlHash = "StitchMakerPathEditor".GetHashCode();
        static readonly Color SelectedPathColor = new Color(0.2f, 0.9f, 1f, 1f);
        static readonly Color OtherPathColor = new Color(0.2f, 0.9f, 1f, 0.35f);
        static readonly Color MirrorPathColor = new Color(0.2f, 0.9f, 1f, 0.4f);
        static readonly Color NodeColor = Color.white;
        static readonly Color SelectedNodeColor = new Color(1f, 0.85f, 0.1f, 1f);
        static readonly Color HandleColor = new Color(1f, 0.4f, 0.8f, 1f);

        static readonly Color GuideColor = new Color(0.3f, 0.95f, 0.45f, 1f);
        static readonly Color GuideBackground = new Color(0.16f, 0.16f, 0.16f, 0.94f);
        static readonly string[] guideLines = new string[3];
        static GUIStyle guideTitleStyle, guideLineStyle;

        static readonly List<Vector3> polyline = new List<Vector3>();
        static Vector3[] lineBuffer = new Vector3[0];

        static DragTarget drag;
        static Matrix4x4 worldToMesh;
        static Vector3 cameraPosition;
        static Vector3 cameraForward;
        static bool cameraOrthographic;

        /// <summary>入力処理とパスのギズモ描画。ドキュメントを変更したら true を返す。</summary>
        public static bool OnSceneGUI(StitchDocument document, MeshSnapshot snapshot,
            Matrix4x4 meshToWorld, ref int selectedPath, ref int selectedNode)
        {
            var camera = Camera.current;
            if (camera == null) return false;

            worldToMesh = meshToWorld.inverse;
            cameraPosition = worldToMesh.MultiplyPoint3x4(camera.transform.position);
            cameraForward = worldToMesh.MultiplyVector(camera.transform.forward);
            cameraOrthographic = camera.orthographic;

            var e = Event.current;
            int id = GUIUtility.GetControlID(ControlHash, FocusType.Passive);
            bool changed = false;

            using (new Handles.DrawingScope(meshToWorld))
            {
                switch (e.GetTypeForControl(id))
                {
                    case EventType.Layout:
                        // シーンのオブジェクト選択を抑止する。
                        HandleUtility.AddDefaultControl(id);
                        break;

                    case EventType.MouseDown:
                        if (e.alt) break;
                        if (e.button == 0)
                            changed = OnLeftDown(document, snapshot, id, ref selectedPath, ref selectedNode);
                        else if (e.button == 1)
                            changed = OnRightDown(document, id, selectedPath, ref selectedNode);
                        break;

                    case EventType.MouseDrag:
                        if (GUIUtility.hotControl != id) break;
                        if (e.button == 0)
                            changed = OnDrag(document, snapshot, selectedPath, selectedNode);
                        e.Use();
                        break;

                    case EventType.MouseUp:
                        if (GUIUtility.hotControl != id) break;
                        GUIUtility.hotControl = 0;
                        drag = DragTarget.None;
                        e.Use();
                        break;

                    case EventType.Repaint:
                        Draw(document, snapshot, selectedPath, selectedNode);
                        break;
                }
            }

            if (e.type == EventType.Repaint) DrawGuide(camera);
            return changed;
        }

        // シーンビュー右下に出す操作ガイド。表示だけで入力は受けない（上をクリックしても編集操作になる）。
        static void DrawGuide(Camera camera)
        {
            if (guideTitleStyle == null)
            {
                guideTitleStyle = new GUIStyle(EditorStyles.boldLabel) { normal = { textColor = Color.white } };
                guideLineStyle = new GUIStyle(EditorStyles.boldLabel) { fontSize = 13, normal = { textColor = GuideColor } };
            }

            guideLines[0] = Loc.T("左クリック：制御点を追加");
            guideLines[1] = Loc.T("ドラッグ：制御点を移動");
            guideLines[2] = Loc.T("右クリック：制御点を削除");
            string title = Loc.T("パス編集モード");

            const float margin = 10f, padding = 8f, titleHeight = 20f, lineHeight = 20f;
            float width = guideTitleStyle.CalcSize(new GUIContent(title)).x;
            foreach (var line in guideLines)
                width = Mathf.Max(width, guideLineStyle.CalcSize(new GUIContent(line)).x);
            width += padding * 2f;
            float height = padding * 2f + titleHeight + lineHeight * guideLines.Length;

            // カメラのピクセルサイズから GUI 座標系での描画領域を求め、右下に寄せる。
            float scale = EditorGUIUtility.pixelsPerPoint;
            var rect = new Rect(camera.pixelWidth / scale - width - margin,
                camera.pixelHeight / scale - height - margin, width, height);

            Handles.BeginGUI();
            EditorGUI.DrawRect(rect, GuideBackground);
            const float border = 2f;
            EditorGUI.DrawRect(new Rect(rect.x, rect.y, rect.width, border), GuideColor);
            EditorGUI.DrawRect(new Rect(rect.x, rect.yMax - border, rect.width, border), GuideColor);
            EditorGUI.DrawRect(new Rect(rect.x, rect.y, border, rect.height), GuideColor);
            EditorGUI.DrawRect(new Rect(rect.xMax - border, rect.y, border, rect.height), GuideColor);

            var row = new Rect(rect.x + padding, rect.y + padding, width - padding * 2f, titleHeight);
            GUI.Label(row, title, guideTitleStyle);
            row.y += titleHeight;
            row.height = lineHeight;
            foreach (var line in guideLines)
            {
                GUI.Label(row, line, guideLineStyle);
                row.y += lineHeight;
            }
            Handles.EndGUI();
        }

        static bool OnLeftDown(StitchDocument document, MeshSnapshot snapshot, int id,
            ref int selectedPath, ref int selectedNode)
        {
            var mouse = Event.current.mousePosition;
            var path = GetPath(document, selectedPath);

            // 優先順位: ノード（ハンドル含む）＞ パスの線 ＞ メッシュ
            if (path != null)
            {
                int node = FindNode(path, mouse);
                if (node >= 0)
                {
                    Undo.RecordObject(document, "Move Stitch Node");
                    selectedNode = node;
                    BeginDrag(DragTarget.Node, id);
                    return false;
                }

                var handle = FindHandle(path, selectedNode, mouse);
                if (handle != DragTarget.None)
                {
                    Undo.RecordObject(document, "Move Stitch Handle");
                    BeginDrag(handle, id);
                    return false;
                }
            }

            if (!snapshot.Raycast(MeshRay(mouse), out var position, out var normal))
            {
                selectedNode = -1;
                return false;
            }

            Undo.RecordObject(document, "Add Stitch Node");
            if (path == null)
            {
                path = document.AddPath();
                selectedPath = document.paths.Count - 1;
                selectedNode = -1;
            }

            int segment = FindSegment(path, mouse);
            int index;
            if (segment >= 0)
                index = segment + 1;
            else if (selectedNode >= 0 && selectedNode < path.nodes.Count - 1)
                index = selectedNode + 1;
            else
                index = path.nodes.Count;

            var added = new StitchNode { position = position, normal = normal };
            path.nodes.Insert(index, added);
            if (path.manualHandles)
                PathSampler.GetAutoHandles(path, index, out added.handleIn, out added.handleOut);

            selectedNode = index;
            BeginDrag(DragTarget.Node, id);
            return true;
        }

        static bool OnRightDown(StitchDocument document, int id, int selectedPath, ref int selectedNode)
        {
            var path = GetPath(document, selectedPath);
            if (path == null) return false;
            int node = FindNode(path, Event.current.mousePosition);
            if (node < 0) return false; // ノード以外はシーンビュー標準の視点操作に渡す

            Undo.RecordObject(document, "Delete Stitch Node");
            path.nodes.RemoveAt(node);
            selectedNode = Mathf.Min(node, path.nodes.Count - 1);

            // MouseUp まで握って、右クリックメニューや視点操作が始まらないようにする。
            BeginDrag(DragTarget.None, id);
            return true;
        }

        static bool OnDrag(StitchDocument document, MeshSnapshot snapshot, int selectedPath, int selectedNode)
        {
            if (drag == DragTarget.None) return false;
            var path = GetPath(document, selectedPath);
            if (path == null || selectedNode < 0 || selectedNode >= path.nodes.Count) return false;

            // メッシュ外では直前の位置を保つ。
            var e = Event.current;
            if (!snapshot.Raycast(MeshRay(e.mousePosition), out var position, out var normal)) return false;

            var node = path.nodes[selectedNode];
            switch (drag)
            {
                case DragTarget.Node:
                    node.position = position;
                    node.normal = normal;
                    break;
                case DragTarget.HandleIn:
                    node.handleIn = position - node.position;
                    if (!e.shift) node.handleOut = -node.handleIn;
                    break;
                case DragTarget.HandleOut:
                    node.handleOut = position - node.position;
                    if (!e.shift) node.handleIn = -node.handleOut;
                    break;
            }
            return true;
        }

        static void BeginDrag(DragTarget target, int id)
        {
            drag = target;
            GUIUtility.hotControl = id;
            Event.current.Use();
        }

        static void Draw(StitchDocument document, MeshSnapshot snapshot, int selectedPath, int selectedNode)
        {
            for (int i = 0; i < document.paths.Count; i++)
            {
                var path = document.paths[i];
                if (!path.visible && i != selectedPath) continue;

                PathSampler.BuildPolyline(path, polyline);
                if (polyline.Count >= 2)
                {
                    if (lineBuffer.Length < polyline.Count) lineBuffer = new Vector3[polyline.Count];
                    polyline.CopyTo(lineBuffer);
                    Handles.color = i == selectedPath ? SelectedPathColor : OtherPathColor;
                    Handles.DrawAAPolyLine(i == selectedPath ? 3f : 2f, polyline.Count, lineBuffer);
                }

                // ミラー側は元パスの従属物なので、線だけを薄く描く（ノードは出さず、編集も受け付けない）。
                if (path.mirrorAxis == MirrorAxis.None) continue;
                PathSampler.BuildMirroredPolyline(path, snapshot, polyline);
                if (polyline.Count < 2) continue;
                if (lineBuffer.Length < polyline.Count) lineBuffer = new Vector3[polyline.Count];
                polyline.CopyTo(lineBuffer);
                Handles.color = MirrorPathColor;
                Handles.DrawAAPolyLine(2f, polyline.Count, lineBuffer);
            }

            var selected = GetPath(document, selectedPath);
            if (selected == null) return;

            for (int i = 0; i < selected.nodes.Count; i++)
            {
                var node = selected.nodes[i];
                var color = i == selectedNode ? SelectedNodeColor : NodeColor;
                if (!IsFacing(node)) color.a = 0.3f; // 裏側のノードは薄く表示
                Handles.color = color;
                DrawDot(node.position, 0.045f);
            }

            if (!selected.manualHandles || selectedNode < 0 || selectedNode >= selected.nodes.Count) return;
            var current = selected.nodes[selectedNode];
            Handles.color = HandleColor;
            if (HasPrev(selected, selectedNode))
            {
                Handles.DrawLine(current.position, current.position + current.handleIn);
                DrawDot(current.position + current.handleIn, 0.035f);
            }
            if (HasNext(selected, selectedNode))
            {
                Handles.DrawLine(current.position, current.position + current.handleOut);
                DrawDot(current.position + current.handleOut, 0.035f);
            }
        }

        static void DrawDot(Vector3 position, float scale)
        {
            Handles.DotHandleCap(0, position, Quaternion.identity,
                HandleUtility.GetHandleSize(position) * scale, EventType.Repaint);
        }

        static StitchPath GetPath(StitchDocument document, int index)
        {
            return index >= 0 && index < document.paths.Count ? document.paths[index] : null;
        }

        static Ray MeshRay(Vector2 mouse)
        {
            var ray = HandleUtility.GUIPointToWorldRay(mouse);
            return new Ray(worldToMesh.MultiplyPoint3x4(ray.origin), worldToMesh.MultiplyVector(ray.direction));
        }

        static bool IsFacing(StitchNode node)
        {
            return cameraOrthographic
                ? Vector3.Dot(node.normal, cameraForward) < 0f
                : Vector3.Dot(node.normal, cameraPosition - node.position) > 0f;
        }

        static bool HasPrev(StitchPath path, int index)
        {
            return PathSampler.IsClosed(path) || index > 0;
        }

        static bool HasNext(StitchPath path, int index)
        {
            return PathSampler.IsClosed(path) || index < path.nodes.Count - 1;
        }

        static int FindNode(StitchPath path, Vector2 mouse)
        {
            int best = -1;
            float bestDistance = NodePickRadius;
            for (int i = 0; i < path.nodes.Count; i++)
            {
                var node = path.nodes[i];
                if (!IsFacing(node)) continue;
                float distance = (HandleUtility.WorldToGUIPoint(node.position) - mouse).magnitude;
                if (distance < bestDistance)
                {
                    bestDistance = distance;
                    best = i;
                }
            }
            return best;
        }

        static DragTarget FindHandle(StitchPath path, int selectedNode, Vector2 mouse)
        {
            if (!path.manualHandles || selectedNode < 0 || selectedNode >= path.nodes.Count)
                return DragTarget.None;

            var node = path.nodes[selectedNode];
            float distanceIn = HasPrev(path, selectedNode)
                ? (HandleUtility.WorldToGUIPoint(node.position + node.handleIn) - mouse).magnitude
                : float.MaxValue;
            float distanceOut = HasNext(path, selectedNode)
                ? (HandleUtility.WorldToGUIPoint(node.position + node.handleOut) - mouse).magnitude
                : float.MaxValue;

            if (Mathf.Min(distanceIn, distanceOut) >= NodePickRadius) return DragTarget.None;
            return distanceIn < distanceOut ? DragTarget.HandleIn : DragTarget.HandleOut;
        }

        // マウスに最も近いセグメント（ノード index → index+1）を返す。範囲外なら -1。
        static int FindSegment(StitchPath path, Vector2 mouse)
        {
            PathSampler.BuildPolyline(path, polyline);
            if (polyline.Count < 2) return -1;

            int best = -1;
            float bestDistance = LinePickRadius;
            var previous = HandleUtility.WorldToGUIPoint(polyline[0]);
            for (int i = 1; i < polyline.Count; i++)
            {
                var current = HandleUtility.WorldToGUIPoint(polyline[i]);
                float distance = HandleUtility.DistancePointToLineSegment(mouse, previous, current);
                if (distance < bestDistance)
                {
                    bestDistance = distance;
                    best = i - 1;
                }
                previous = current;
            }
            if (best < 0) return -1;
            return Mathf.Min(best / PathSampler.Subdivisions, PathSampler.SegmentCount(path) - 1);
        }
    }
}
