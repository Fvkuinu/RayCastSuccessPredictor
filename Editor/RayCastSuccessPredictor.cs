using UnityEditor;
using UnityEngine;
using System;
using System.Collections.Generic;

/// <summary>
/// Unityエディタ上で、指定したカメラからターゲットオブジェクトを見た際の成功率を予測・表示するツール。
/// 英語と日本語の表示切り替え、および表示のON/OFF機能を備えています。
/// </summary>
public class RayCastSuccessPredictor : EditorWindow
{
    // --- 定数定義 ---
    private const double g = -0.0623;
    private const double h = -0.0846;

    // --- 多言語対応用のテキスト管理 ---
    private static bool IsJapanese => Application.systemLanguage == SystemLanguage.Japanese;

    private static class Loc
    {
        public static string WindowTitle => IsJapanese ? "成功率予測ツール" : "Success Predictor";
        public static string BasicSettings => IsJapanese ? "基本設定" : "Basic Settings";
        public static string RefCamera => IsJapanese ? "基準カメラ" : "Reference Camera";
        public static string LabelColor => IsJapanese ? "ラベル色" : "Label Color";
        public static string TargetShape => IsJapanese ? "ターゲット形状" : "Target Shape";
        public static string CalcModel => IsJapanese ? "座標系と計算モデル" : "Coordinate System & Model";
        public static string CoordSystem => IsJapanese ? "座標系" : "Coord System";
        public static string Pointer => IsJapanese ? "ポインター" : "Pointer";
        public static string UseDistance => IsJapanese ? "距離(A)を使用" : "Use Distance (A)";
        public static string UseMux => IsJapanese ? "muxを使用" : "Use mux";
        public static string ShowInScene => IsJapanese ? "シーンビューに情報を表示" : "Show Info in Scene View";
        public static string HelpText => IsJapanese ? "シーンビューで対象を選択して成功率を確認します。" : "Select an object in Scene View to check success rate.";
        
        public static string ApparentSize => IsJapanese ? "見かけサイズ" : "Apparent Size";
        public static string AngleDist => IsJapanese ? "角度距離(A)" : "Angular Distance (A)";
        public static string SuccessRate => IsJapanese ? "成功率" : "Success Rate";
        public static string NoRenderer => IsJapanese ? "ターゲットにRendererがありません" : "No Renderer/RectTransform found";
    }

    // --- エディタUIの状態を管理する列挙型 ---
    private enum Shape { Circle, Rectangle }
    private enum CoordSystem { PointerBased, WorldSpace }
    private enum LabelColor { Red, Green, Blue }

    // --- UIで設定される変数 ---
    private Camera referenceCamera;
    private GameObject pointerObject;
    private Shape shapeSelection = Shape.Circle;
    private CoordSystem coordSystem = CoordSystem.WorldSpace;
    private LabelColor selectedColor = LabelColor.Red;
    private bool useDistance = true;
    private bool useMux = true;
    private bool showInfoInScene = true;

    private GUIStyle labelStyle;
    private GUIStyle headerStyle;

    [MenuItem("Window/Custom Tools/RayCastSuccessPredictor")]
    public static void ShowWindow()
    {
        GetWindow<RayCastSuccessPredictor>(Loc.WindowTitle);
    }

    private void OnEnable()
    {
        SceneView.duringSceneGui += OnSceneGUI;
    }

    private void OnDisable()
    {
        SceneView.duringSceneGui -= OnSceneGUI;
    }

    private void OnGUI()
    {
        if (headerStyle == null)
            headerStyle = new GUIStyle(EditorStyles.boldLabel) { fontSize = 13 };
        
        if (labelStyle == null)
            labelStyle = new GUIStyle { fontSize = 14, fontStyle = FontStyle.Bold };

        GUILayout.Label(Loc.WindowTitle, headerStyle);
        
        EditorGUILayout.Space();
        showInfoInScene = EditorGUILayout.ToggleLeft(Loc.ShowInScene, showInfoInScene, EditorStyles.boldLabel);
        EditorGUILayout.Space();

        EditorGUILayout.LabelField(Loc.BasicSettings, EditorStyles.boldLabel);
        referenceCamera = (Camera)EditorGUILayout.ObjectField(Loc.RefCamera, referenceCamera, typeof(Camera), true);
        selectedColor = (LabelColor)EditorGUILayout.EnumPopup(Loc.LabelColor, selectedColor);
        shapeSelection = (Shape)EditorGUILayout.EnumPopup(Loc.TargetShape, shapeSelection);
        
        EditorGUILayout.Space();

        if (shapeSelection == Shape.Rectangle)
        {
            coordSystem = CoordSystem.WorldSpace;
            useDistance = false;
            useMux = false;
        }
        else 
        {
            EditorGUILayout.LabelField(Loc.CalcModel, EditorStyles.boldLabel);
            coordSystem = (CoordSystem)EditorGUILayout.EnumPopup(Loc.CoordSystem, coordSystem);

            if (coordSystem == CoordSystem.PointerBased)
                pointerObject = (GameObject)EditorGUILayout.ObjectField(Loc.Pointer, pointerObject, typeof(GameObject), true);

            EditorGUI.BeginDisabledGroup(coordSystem == CoordSystem.WorldSpace);
            useDistance = EditorGUILayout.Toggle(Loc.UseDistance, useDistance);
            EditorGUI.EndDisabledGroup();
            useMux = EditorGUILayout.Toggle(Loc.UseMux, useMux);
        }

        EditorGUILayout.Space();
        EditorGUILayout.HelpBox(Loc.HelpText, MessageType.Info);
    }

    private void OnSceneGUI(SceneView sceneView)
    {
        if (!showInfoInScene || labelStyle == null) return;

        switch (selectedColor)
        {
            case LabelColor.Red: labelStyle.normal.textColor = new Color(1f, 0.2f, 0.2f); break;
            case LabelColor.Green: labelStyle.normal.textColor = new Color(0.2f, 0.8f, 0.2f); break;
            case LabelColor.Blue: labelStyle.normal.textColor = new Color(0.3f, 0.6f, 1f); break;
        }
        
        if (referenceCamera == null || Selection.activeGameObject == null) return;

        GameObject selectedObject = Selection.activeGameObject;
        if (referenceCamera.gameObject == selectedObject) return;
        if (coordSystem == CoordSystem.PointerBased && pointerObject == null) return;

        Transform cameraTransform = referenceCamera.transform;
        Vector3 cameraPos = cameraTransform.position;
        Vector3 targetPos = selectedObject.transform.position;
        float z = Vector3.Distance(cameraPos, targetPos);
        if (z < 0.01f) return;

        float W, H, r = 0;
        Vector3[] highlightCorners;

        if (coordSystem == CoordSystem.PointerBased)
        {
            Vector3 sizeViewDirection = (targetPos - cameraPos).normalized;
            List<Vector3> worldPoints = GetWorldPoints(selectedObject);
            if (worldPoints == null)
            {
                Handles.Label(targetPos, Loc.NoRenderer, labelStyle);
                return;
            }
            highlightCorners = CalculateProjectedSize(worldPoints, targetPos, sizeViewDirection, cameraTransform.up, out float pW, out float pH);
            if (shapeSelection == Shape.Rectangle)
            {
                W = 2 * Mathf.Atan2(pW / 2f, z) * Mathf.Rad2Deg;
                H = 2 * Mathf.Atan2(pH / 2f, z) * Mathf.Rad2Deg;
            }
            else
            {
                float diameter = Mathf.Min(pW, pH);
                W = H = 2 * Mathf.Atan2(diameter / 2f, z) * Mathf.Rad2Deg;
                r = diameter / 2.0f;
            }
        }
        else 
        {
            RectTransform rectTransform = selectedObject.GetComponent<RectTransform>();
            if (rectTransform != null)
            {
                float worldWidth = rectTransform.rect.width * rectTransform.lossyScale.x;
                float worldHeight = rectTransform.rect.height * rectTransform.lossyScale.y;
                W = 2 * Mathf.Atan2(worldWidth / 2f, z) * Mathf.Rad2Deg;
                H = 2 * Mathf.Atan2(worldHeight / 2f, z) * Mathf.Rad2Deg;
                if (shapeSelection == Shape.Circle) W = H = Mathf.Min(W, H);
                highlightCorners = new Vector3[4];
                rectTransform.GetWorldCorners(highlightCorners);
            }
            else
            {
                Renderer targetRenderer = selectedObject.GetComponent<Renderer>();
                if (targetRenderer == null) return;
                Bounds bounds = targetRenderer.bounds;
                W = 2 * Mathf.Atan2(bounds.size.x, 2 * z) * Mathf.Rad2Deg;
                H = 2 * Mathf.Atan2(bounds.size.y, 2 * z) * Mathf.Rad2Deg;
                if (shapeSelection == Shape.Circle) W = H = Mathf.Min(W, H);
                Vector3 up = cameraTransform.up * bounds.extents.y;
                Vector3 right = cameraTransform.right * bounds.extents.x;
                highlightCorners = new Vector3[] { targetPos - right + up, targetPos + right + up, targetPos + right - up, targetPos - right - up };
            }
        }

        float A = 0;
        if (coordSystem == CoordSystem.PointerBased)
        {
            Vector3 newForward = (pointerObject.transform.position - cameraPos).normalized;
            Vector3 targetDir = (targetPos - cameraPos).normalized;
            A = Vector3.Angle(newForward, targetDir);
        }

        double mux = useMux ? (g * W + h) : 0;
        double sigma_x, sigma_y;
        if (coordSystem == CoordSystem.WorldSpace)
        {
            sigma_x = 0.1660 + 0.1011 * W;
            sigma_y = 0.2481 + 0.1036 * (shapeSelection == Shape.Rectangle ? H : W);
        }
        else
        {
            if (useDistance)
            {
                sigma_x = (0.1102 * W) + (0.0017 * A) + 0.1548;
                sigma_y = (0.0715 * (shapeSelection == Shape.Rectangle ? H : W)) + (0.00037 * A) + 0.2439;
            }
            else
            {
                sigma_x = 0.2130 + 0.1102 * W;
                sigma_y = 0.2311 + 0.0715 * (shapeSelection == Shape.Rectangle ? H : W);
            }
        }

        double successRate = ComputeSuccessRate(W, H, shapeSelection, mux, sigma_x, sigma_y);
        DrawHighlights(targetPos, highlightCorners, r, shapeSelection, (targetPos - cameraPos).normalized);
        DrawInfoText(targetPos, cameraPos, W, H, A, successRate);
    }

    private List<Vector3> GetWorldPoints(GameObject obj)
    {
        RectTransform rt = obj.GetComponent<RectTransform>();
        if (rt != null)
        {
            Vector3[] corners = new Vector3[4];
            rt.GetWorldCorners(corners);
            return new List<Vector3>(corners);
        }
        Renderer rend = obj.GetComponent<Renderer>();
        if (rend != null)
        {
            Bounds b = rend.bounds;
            return new List<Vector3> {
                b.min, b.max,
                new Vector3(b.min.x, b.min.y, b.max.z), new Vector3(b.min.x, b.max.y, b.min.z),
                new Vector3(b.max.x, b.min.y, b.min.z), new Vector3(b.min.x, b.max.y, b.max.z),
                new Vector3(b.max.x, b.min.y, b.max.z), new Vector3(b.max.x, b.max.y, b.min.z)
            };
        }
        return null;
    }

    private Vector3[] CalculateProjectedSize(List<Vector3> worldPoints, Vector3 center, Vector3 viewDirection, Vector3 worldUp, out float width, out float height)
    {
        Vector3 projUp = Vector3.ProjectOnPlane(worldUp, viewDirection).normalized;
        Vector3 projRight = Vector3.Cross(projUp, viewDirection);
        float minX = float.MaxValue, maxX = float.MinValue, minY = float.MaxValue, maxY = float.MinValue;
        foreach (Vector3 point in worldPoints)
        {
            Vector3 vecFromCenter = point - center;
            float px = Vector3.Dot(vecFromCenter, projRight); float py = Vector3.Dot(vecFromCenter, projUp);
            if (px < minX) minX = px; if (px > maxX) maxX = px; if (py < minY) minY = py; if (py > maxY) maxY = py;
        }
        width = maxX - minX; height = maxY - minY;
        return new Vector3[] { 
            center + projRight * minX + projUp * minY, 
            center + projRight * minX + projUp * maxY, 
            center + projRight * maxX + projUp * maxY, 
            center + projRight * maxX + projUp * minY 
        };
    }

    private void DrawHighlights(Vector3 targetPos, Vector3[] corners, float circleRadius, Shape shape, Vector3 viewNormal)
    {
        Handles.color = labelStyle.normal.textColor;
        if (shape == Shape.Rectangle)
            Handles.DrawPolyLine(corners[0], corners[1], corners[2], corners[3], corners[0]);
        else
            Handles.DrawWireDisc(targetPos, viewNormal, circleRadius);
    }

    private void DrawInfoText(Vector3 targetPos, Vector3 cameraPos, float W, float H, float A, double successRate)
    {
        string sizeText = shapeSelection == Shape.Rectangle ? $"W: {W:F2}°, H: {H:F2}°" : $"{W:F2}°";
        string distText = (coordSystem == CoordSystem.PointerBased && useDistance) ? $"{Loc.AngleDist}: {A:F2}°\n" : "";
        string infoText = $"{Loc.ApparentSize}: {sizeText}\n{distText}{Loc.SuccessRate}: {successRate:F2}%";

        Vector3 textPos = targetPos + (targetPos - cameraPos).normalized;
        Handles.Label(textPos, infoText, labelStyle);
    }

    private double ComputeSuccessRate(float W, float H, Shape shape, double mux, double sigma_x, double sigma_y)
    {
        if (sigma_x < 1e-6) sigma_x = 1e-6; if (sigma_y < 1e-6) sigma_y = 1e-6;
        double totalProbability = 0; int steps = 100;
        float x_min = -W / 2.0f; float y_min = -H / 2.0f;
        float stepSizeX = W / steps; float stepSizeY = H / steps;
        double areaElement = stepSizeX * stepSizeY;
        for (int i = 0; i < steps; i++) {
            double x = x_min + (i + 0.5) * stepSizeX;
            for (int j = 0; j < steps; j++) {
                double y = y_min + (j + 0.5) * stepSizeY;
                bool inArea = (shape == Shape.Rectangle) || (Math.Pow(x, 2) + Math.Pow(y, 2) <= Math.Pow(W / 2.0, 2));
                if (inArea) {
                    double exp_term = Math.Exp(-(Math.Pow(x - mux, 2) / (2 * sigma_x * sigma_x)) - (Math.Pow(y, 2) / (2 * sigma_y * sigma_y)));
                    totalProbability += (1.0 / (2 * Math.PI * sigma_x * sigma_y)) * exp_term * areaElement;
                }
            }
        }
        return totalProbability * 100.0;
    }
}