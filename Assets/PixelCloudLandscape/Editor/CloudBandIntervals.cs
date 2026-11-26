using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace TA.PixelCloudLandscape
{
    internal sealed class CloudBandIntervals
    {
        // Index of the boundary between the selected band and the one before it.
        int edge = 1;
        int undoGroup;

        internal void Draw(SerializedProperty dark, SerializedProperty light, int steps)
        {
            var serialized = dark.serializedObject;
            var cloud = (LandscapeClouds)serialized.targetObject;
            var spacing = serialized.FindProperty("bandSpacing");
            ReadOnlySpan<Vector2> knots = cloud.BandSpacing;
            Rect strip = EditorGUILayout.GetControlRect(false, 22);
            if (Event.current.type == EventType.Repaint)
                Paint(strip, knots, steps, LandscapeClouds.CurvePoint(dark.colorValue, false),
                    LandscapeClouds.CurvePoint(light.colorValue, true));
            if (steps < 2) return;

            edge = Mathf.Clamp(edge, 1, steps - 1);
            float position = Boundary(knots, edge, steps);
            int id = GUIUtility.GetControlID(FocusType.Passive, strip);
            Event e = Event.current;
            EditorGUIUtility.AddCursorRect(strip, MouseCursor.ResizeHorizontal);
            switch (e.GetTypeForControl(id))
            {
                case EventType.Repaint:
                    float x = strip.x + position * strip.width;
                    EditorGUI.DrawRect(new Rect(x-2, strip.yMax-7, 4, 7), Color.black);
                    EditorGUI.DrawRect(new Rect(x-1, strip.yMax-6, 2, 6), Color.white);
                    break;
                case EventType.MouseDown when e.button == 0 && strip.Contains(e.mousePosition):
                    float click = Mathf.InverseLerp(strip.x, strip.xMax, e.mousePosition.x);
                    float mapped = LandscapeClouds.MapBandSpacing(knots, click, true);
                    int right = (int)Math.Min(steps-1, Math.Floor((double)mapped*steps)+1);
                    int left = Mathf.Max(1, right-1);
                    edge = Mathf.Abs(Boundary(knots,left,steps)-click) < Mathf.Abs(Boundary(knots,right,steps)-click)
                        ? left : right;
                    GUIUtility.hotControl = id;
                    Undo.IncrementCurrentGroup();
                    undoGroup = Undo.GetCurrentGroup();
                    Undo.SetCurrentGroupName("移动云色分界");
                    e.Use();
                    break;
                case EventType.MouseDrag when GUIUtility.hotControl == id:
                    Move(spacing, knots, steps, edge, Mathf.InverseLerp(strip.x, strip.xMax, e.mousePosition.x));
                    e.Use();
                    break;
                case EventType.MouseUp when GUIUtility.hotControl == id:
                    GUIUtility.hotControl = 0;
                    Undo.CollapseUndoOperations(undoGroup);
                    e.Use();
                    break;
            }

            int number = EditorGUILayout.DelayedIntField("编辑第几阶起点", edge+1);
            edge = Mathf.Clamp(number, 2, steps)-1;
            position = Boundary(knots, edge, steps);
            EditorGUI.BeginChangeCheck();
            float next = EditorGUILayout.DelayedFloatField("起始亮度", position);
            if (EditorGUI.EndChangeCheck()) Move(spacing, knots, steps, edge, next);
            EditorGUILayout.LabelField("在色条上左右拖动分界，或输入该阶的起始亮度。", EditorStyles.wordWrappedMiniLabel);
            using (new EditorGUI.DisabledScope(spacing.arraySize == 0))
                if (GUILayout.Button("恢复等宽区间")) spacing.ClearArray();
        }

        static float Boundary(ReadOnlySpan<Vector2> knots, int index, int steps) =>
            LandscapeClouds.MapBandSpacing(knots, index / (float)steps, false);

        internal static void Move(SerializedProperty spacing, ReadOnlySpan<Vector2> knots,
            int steps, int index, float brightness)
        {
            float left = (index-1)/(float)steps, center = index/(float)steps, right = (index+1)/(float)steps;
            float low = LandscapeClouds.MapBandSpacing(knots, left, false);
            float high = LandscapeClouds.MapBandSpacing(knots, right, false);
            if (!(left < center && center < right && low < brightness && brightness < high)) return;

            // Pin the two neighbouring edges, preserving all other intervals at this step count.
            var points = new List<Vector2>(knots.Length+3);
            foreach (var p in knots) if (p.x < left) points.Add(p);
            if (left > 0) points.Add(new Vector2(left,low));
            points.Add(new Vector2(center,brightness));
            if (right < 1) points.Add(new Vector2(right,high));
            foreach (var p in knots) if (p.x > right) points.Add(p);
            spacing.arraySize = points.Count;
            for (int i = 0; i < points.Count; i++)
                spacing.GetArrayElementAtIndex(i).vector2Value = points[i];
        }

        static void Paint(Rect strip, ReadOnlySpan<Vector2> knots, int steps, Vector3 p1, Vector3 p2)
        {
            int pixels = Mathf.Max(1, Mathf.CeilToInt(strip.width));
            bool exact = steps > 0 && steps <= pixels;
            int samples = exact ? steps : pixels;
            for (int i=0; i<samples; i++)
            {
                float left = exact ? Boundary(knots,i,steps) : i/(float)samples;
                float right = exact ? Boundary(knots,i+1,steps) : (i+1)/(float)samples;
                float t = (left+right)*.5f;
                if (steps == 1) t = .5f;
                if (steps > 1)
                {
                    double band = exact ? i : Math.Min(steps-1, Math.Floor(
                        (double)LandscapeClouds.MapBandSpacing(knots,t,true)*steps));
                    t = (float)(band/(steps-1));
                }
                float s = 1-t;
                Vector3 rgb = 3*s*s*t*p1 + 3*s*t*t*p2 + t*t*t*Vector3.one;
                Color response = new Color(rgb.x,rgb.y,rgb.z).gamma;
                EditorGUI.DrawRect(Rect.MinMaxRect(Mathf.Round(strip.x+left*strip.width),strip.y,
                    Mathf.Round(strip.x+right*strip.width),strip.yMax),response);
            }
        }
    }
}
