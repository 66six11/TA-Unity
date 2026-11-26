using UnityEditor;
using UnityEngine;

namespace TA.PixelCloudLandscape
{
    // Both plots edit the component's serialized bands directly, just like the numeric fields.
    internal sealed class CloudBandPlot
    {
        readonly bool light;
        int dragged = -1;
        int undoGroup;
        float axisEnd = 2;
        Vector2 grabOffset;
        Color grabColor;
        static readonly Color Background = new(0.13f,0.14f,0.16f);
        static readonly Color Grid = new(0.28f,0.30f,0.33f);
        static readonly Color AlphaLine = new(0.43f,0.79f,0.91f);

        internal CloudBandPlot(bool light) => this.light = light;

        internal void Draw(SerializedProperty bands)
        {
            if (bands.arraySize == 0) return;
            if (!light)
            {
                float extent = EditorGUILayout.DelayedFloatField("厚度图横轴上限", axisEnd);
                if (float.IsFinite(extent) && extent > 0) axisEnd = extent;
                float last = Start(bands,bands.arraySize-1);
                if (last > axisEnd && dragged < 0) axisEnd = last * 1.15f;
            }
            Rect area = EditorGUILayout.GetControlRect(false, 106);
            DrawAt(area,bands);
        }

        internal void DrawAt(Rect area, SerializedProperty bands)
        {
            float end = light ? 1 : axisEnd;
            Rect plot = new(area.x+18, area.y+6, Mathf.Max(16,area.width-27), area.height-32);
            int id = GUIUtility.GetControlID(FocusType.Passive,plot);
            if (Event.current.type == EventType.Repaint)
            {
                EditorGUI.DrawRect(plot,Background);
                for (int n=1;n<4;n++)
                {
                    EditorGUI.DrawRect(new Rect(plot.x+plot.width*n/4,plot.y,1,plot.height),Grid);
                    EditorGUI.DrawRect(new Rect(plot.x,plot.y+plot.height*n/4,plot.width,1),Grid);
                }
                for (int i=0;i<bands.arraySize;i++)
                {
                    var entry=bands.GetArrayElementAtIndex(i);
                    Color ink=light ? entry.FindPropertyRelative("color").colorValue : AlphaLine;
                    Vector2 p=Node(plot,bands,i,end);
                    float next=i+1<bands.arraySize ? Node(plot,bands,i+1,end).x : plot.xMax;
                    var fill=ink; fill.a=light ? 0.46f : 0.17f;
                    EditorGUI.DrawRect(new Rect(p.x,p.y,next-p.x,plot.yMax-p.y),fill);
                    EditorGUI.DrawRect(new Rect(p.x,p.y-1,next-p.x,2),ink);
                    if(i>0)
                    {
                        float y=Node(plot,bands,i-1,end).y;
                        EditorGUI.DrawRect(new Rect(p.x-1,Mathf.Min(y,p.y),2,Mathf.Abs(y-p.y)),ink);
                    }
                }
                for(int i=0;i<bands.arraySize;i++)
                {
                    Vector2 p=Node(plot,bands,i,end);
                    EditorGUI.DrawRect(new Rect(p.x-4,p.y-4,8,8), i==dragged ? Color.yellow : Color.white);
                    EditorGUI.DrawRect(new Rect(p.x-2,p.y-2,4,4),Background);
                }
            }
            GUI.Label(new Rect(area.x,plot.y-5,18,16),"1",EditorStyles.miniLabel);
            GUI.Label(new Rect(area.x,plot.yMax-9,18,16),"0",EditorStyles.miniLabel);
            GUI.Label(new Rect(plot.x,plot.yMax+4,28,18),"0",EditorStyles.miniLabel);
            GUI.Label(new Rect(plot.x,plot.yMax+4,plot.width,18),
                light ? "亮度 →    纵轴为颜色明度" : "视线厚度 τ →    纵轴为不透明度",EditorStyles.centeredGreyMiniLabel);
            var endLabel=new GUIContent(end.ToString("G3"));
            float width=EditorStyles.miniLabel.CalcSize(endLabel).x+3;
            GUI.Label(new Rect(plot.xMax-width+2,plot.yMax+4,width,18),endLabel,EditorStyles.miniLabel);
            HandleInput(plot,bands,end,id);
        }

        static float Start(SerializedProperty bands,int i) =>
            bands.GetArrayElementAtIndex(i).FindPropertyRelative("start").floatValue;

        Vector2 Node(Rect plot,SerializedProperty bands,int i,float end)
        {
            var entry=bands.GetArrayElementAtIndex(i);
            float value=light ? entry.FindPropertyRelative("color").colorValue.grayscale
                              : entry.FindPropertyRelative("opacity").floatValue;
            return new Vector2(plot.x+plot.width*Mathf.Clamp01(Start(bands,i)/end),
                               plot.yMax-plot.height*Mathf.Clamp01(value));
        }

        void HandleInput(Rect plot,SerializedProperty bands,float end,int id)
        {
            var e=Event.current;
            int nearest=-1;
            float best=100;
            for(int i=0;i<bands.arraySize;i++)
            {
                Vector2 p=Node(plot,bands,i,end);
                EditorGUIUtility.AddCursorRect(new Rect(p.x-6,p.y-6,12,12),
                    i==0 ? MouseCursor.ResizeVertical : MouseCursor.MoveArrow);
                float d=(p-e.mousePosition).sqrMagnitude;
                if(d<best) { best=d; nearest=i; }
            }
            switch(e.GetTypeForControl(id))
            {
                case EventType.MouseDown when e.button==0 && nearest>=0:
                    GUIUtility.hotControl=id;
                    dragged=nearest;
                    grabOffset=e.mousePosition-Node(plot,bands,nearest,end);
                    if(light) grabColor=bands.GetArrayElementAtIndex(nearest).FindPropertyRelative("color").colorValue;
                    Undo.IncrementCurrentGroup();
                    undoGroup=Undo.GetCurrentGroup();
                    Undo.SetCurrentGroupName(light ? "调整云色分界" : "调整云透明度分阶");
                    e.Use();
                    break;
                case EventType.MouseDrag when GUIUtility.hotControl==id && dragged>=0 && dragged<bands.arraySize:
                    var entry=bands.GetArrayElementAtIndex(dragged);
                    if(dragged>0)
                    {
                        float prev=Start(bands,dragged-1);
                        float next=dragged+1<bands.arraySize ? Start(bands,dragged+1) : end;
                        float gap=Mathf.Min(end*0.00001f,(next-prev)*0.25f);
                        float x=(e.mousePosition.x-grabOffset.x-plot.x)/plot.width*end;
                        entry.FindPropertyRelative("start").floatValue=Mathf.Clamp(x,prev+gap,
                            dragged+1<bands.arraySize ? next-gap : end);
                    }
                    float y=Mathf.Clamp01((plot.yMax-e.mousePosition.y+grabOffset.y)/plot.height);
                    if(light)
                    {
                        float from=grabColor.grayscale;
                        Color adjusted=y>=from
                            ? Color.Lerp(grabColor,Color.white,(y-from)/Mathf.Max(1e-6f,1-from))
                            : Color.Lerp(Color.black,grabColor,y/Mathf.Max(1e-6f,from));
                        adjusted.a=grabColor.a;
                        entry.FindPropertyRelative("color").colorValue=adjusted;
                    }
                    else entry.FindPropertyRelative("opacity").floatValue=y;
                    GUI.changed=true;
                    e.Use();
                    break;
                case EventType.MouseUp when GUIUtility.hotControl==id:
                    GUIUtility.hotControl=0;
                    dragged=-1;
                    Undo.CollapseUndoOperations(undoGroup);
                    e.Use();
                    break;
            }
        }
    }
}

