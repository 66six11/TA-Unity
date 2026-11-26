using UnityEditor;
using UnityEngine;

namespace TA.PixelCloudLandscape
{
    internal sealed class CloudBezierPlot
    {
        static readonly string[] Channels = { "R 红", "G 绿", "B 蓝" };
        static readonly Color[] Ink = {
            new(1f, .40f, .36f), new(.38f, .90f, .51f), new(.38f, .65f, 1f)
        };
        readonly CloudBandIntervals intervals = new();
        int channel;
        int dragged;
        int undoGroup;
        float grabOffset;
        float dragMin, dragMax;

        internal void Draw(SerializedProperty dark, SerializedProperty light, int steps)
        {
            EditorGUILayout.LabelField("RGB 贝塞尔响应", EditorStyles.boldLabel);
            channel = GUILayout.Toolbar(channel, Channels);
            Rect area = EditorGUILayout.GetControlRect(false, 129);
            Rect plot = new(area.x + 8, area.y + 7, Mathf.Max(1, area.width - 16), area.height - 16);
            DrawPlot(plot, dark, light, steps);
            intervals.Draw(dark, light, steps);
            EditValue("暗侧控制点", dark, false);
            EditValue("亮侧控制点", light, true);
            EditorGUILayout.LabelField("阶数 0 为连续，1 为单色。", EditorStyles.miniLabel);
            EditorGUILayout.LabelField("横轴为受光亮度，纵轴为通道响应。上下拖动方点调整曲线。", EditorStyles.wordWrappedMiniLabel);
            if (steps == 2)
                EditorGUILayout.LabelField("两阶只采样曲线端点。增加阶数可看到中间色变化。", EditorStyles.wordWrappedMiniLabel);
        }

        void EditValue(string label, SerializedProperty property, bool bright)
        {
            float y = LandscapeClouds.CurvePoint(property.colorValue, bright)[channel];
            EditorGUI.BeginChangeCheck();
            y = EditorGUILayout.FloatField(label, y);
            if (EditorGUI.EndChangeCheck() && float.IsFinite(y))
                SetValue(property, bright, y);
        }

        void SetValue(SerializedProperty property, bool bright, float y)
        {
            var color = property.colorValue;
            color[channel] = LandscapeClouds.EncodeControl(2*y - (bright ? 1 : 0));
            if (float.IsFinite(color[channel])) property.colorValue = color;
        }

        internal void DrawPlot(Rect plot, SerializedProperty dark, SerializedProperty light, int steps)
        {
            Vector3 p1 = LandscapeClouds.CurvePoint(dark.colorValue, false);
            Vector3 p2 = LandscapeClouds.CurvePoint(light.colorValue, true);
            Vector3 low = Vector3.Min(Vector3.zero, Vector3.Min(p1, p2));
            Vector3 high = Vector3.Max(Vector3.one, Vector3.Max(p1, p2));
            float min = Mathf.Min(low.x, Mathf.Min(low.y, low.z));
            float max = Mathf.Max(high.x, Mathf.Max(high.y, high.z));
            Vector2 Point(float t, float y) => new(plot.x + t * plot.width, plot.yMax - Mathf.InverseLerp(min, max, y) * plot.height);
            Vector2 start = Point(0, 0), end = Point(1, 1);
            Vector2 a = Point(1f/3, p1[channel]), b = Point(2f/3, p2[channel]);
            var e = Event.current;
            int id = GUIUtility.GetControlID(FocusType.Passive, plot);
            Rect Handle(Vector2 p) => new(p.x - 6, p.y - 6, 12, 12);
            EditorGUIUtility.AddCursorRect(Handle(a), MouseCursor.ResizeVertical);
            EditorGUIUtility.AddCursorRect(Handle(b), MouseCursor.ResizeVertical);

            switch (e.GetTypeForControl(id))
            {
                case EventType.Repaint:
                    EditorGUI.DrawRect(plot, new Color(.10f, .11f, .13f));
                    for (int i = 1; i < 4; i++)
                    {
                        Vector2 grid = new(plot.x + plot.width*i*.25f, plot.yMax - plot.height*i*.25f);
                        EditorGUI.DrawRect(new Rect(grid.x, plot.y, 1, plot.height), Color.gray * .5f);
                        EditorGUI.DrawRect(new Rect(plot.x, grid.y, plot.width, 1), Color.gray * .5f);
                    }
                    GUI.Label(new Rect(plot.x+2, plot.y, 80, 16), max.ToString("G4"), EditorStyles.whiteMiniLabel);
                    GUI.Label(new Rect(plot.x+2, plot.yMax-16, 80, 16), min.ToString("G4"), EditorStyles.whiteMiniLabel);
                    Handles.BeginGUI();
                    using (new Handles.DrawingScope(Color.gray))
                    {
                        Handles.DrawLine(start, a);
                        Handles.DrawLine(b, end);
                    }
                    // Draw the active channel last so it remains visible when curves coincide.
                    for (int j = 1; j <= 3; j++)
                    {
                        int c = (channel + j) % 3;
                        Handles.DrawBezier(start, end, Point(1f/3, p1[c]), Point(2f/3, p2[c]),
                            Ink[c], null, c == channel ? 3 : 1.5f);
                    }
                    Handles.EndGUI();
                    EditorGUI.DrawRect(Handle(a), Ink[channel]);
                    EditorGUI.DrawRect(Handle(b), Ink[channel]);
                    break;
                case EventType.MouseDown when e.button == 0 && (Handle(a).Contains(e.mousePosition) || Handle(b).Contains(e.mousePosition)):
                    dragged = Handle(a).Contains(e.mousePosition) ? 1 : 2;
                    dragMin = min;
                    dragMax = max;
                    grabOffset = e.mousePosition.y - (dragged == 1 ? a.y : b.y);
                    GUIUtility.hotControl = id;
                    Undo.IncrementCurrentGroup();
                    undoGroup = Undo.GetCurrentGroup();
                    Undo.SetCurrentGroupName("编辑云色贝塞尔曲线");
                    e.Use();
                    break;
                case EventType.MouseDrag when GUIUtility.hotControl == id:
                    var property = dragged == 1 ? dark : light;
                    float y = (plot.yMax - e.mousePosition.y + grabOffset) / plot.height;
                    SetValue(property, dragged == 2, Mathf.LerpUnclamped(dragMin, dragMax, y));
                    GUI.changed = true;
                    e.Use();
                    break;
                case EventType.MouseUp when GUIUtility.hotControl == id:
                    GUIUtility.hotControl = 0;
                    Undo.CollapseUndoOperations(undoGroup);
                    e.Use();
                    break;
            }
        }
    }
}
