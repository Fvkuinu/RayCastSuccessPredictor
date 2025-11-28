using UnityEditor;
using UnityEngine;
using System;
using System.Collections.Generic;

/// <summary>
/// Unityエディタ上で、指定したカメラからターゲットオブジェクト（3D/2D UI）を見た際の
/// 各種パラメータとヒット成功率を計算・表示するカスタムウィンドウ。
/// PointerBasedモードでは視線に対するターゲットの回転を考慮した見かけのサイズを計算します。
/// </summary>
public class RayCastSuccessPredictor : EditorWindow
{
    // --- 定数定義 ---
    private const double g = -0.0623;
    private const double h = -0.0846;

    // --- エディタUIの状態を管理する列挙型 ---
    private enum Shape { Circle, Rectangle }
    private enum CoordSystem { PointerBased, WorldSpace }
    private enum LabelColor { Red, Green, Blue } // [追加] 色選択用の列挙型

    // --- UIで設定される変数 ---
    private Camera referenceCamera;
    private GameObject pointerObject;
    private Shape shapeSelection = Shape.Circle;
    private CoordSystem coordSystem = CoordSystem.WorldSpace;
    private LabelColor selectedColor = LabelColor.Red; // [追加] 選択された色を保持する変数
    private bool useDistance = true;
    private bool useMux = true;

    // --- UI表示用のスタイル ---
    private GUIStyle labelStyle;
    private GUIStyle headerStyle;

    [MenuItem("Window/Custom Tools/RayCastSuccessPredictor")]
    public static void ShowWindow()
    {
        GetWindow<RayCastSuccessPredictor>("RayCastSuccessPredictor");
    }

    private void OnEnable()
    {
        SceneView.duringSceneGui += OnSceneGUI;
    }

    private void OnDisable()
    {
        SceneView.duringSceneGui -= OnSceneGUI;
    }

    /// <summary>
    /// エディタウィンドウのUIを描画します。
    /// ターゲット形状に応じてUIの表示/非表示を切り替えます。
    /// </summary>
    private void OnGUI()
    {
        if (headerStyle == null)
        {
            headerStyle = new GUIStyle(EditorStyles.boldLabel) { fontSize = 13 };
        }
        if (labelStyle == null)
        {
            // [変更] 初期色はここで設定せず、OnSceneGUIで動的に設定するようにします。
            // そのため、ここではインスタンスを作成するだけです。
            labelStyle = new GUIStyle
            {
                fontSize = 14,
                fontStyle = FontStyle.Bold,
            };
        }

        GUILayout.Label("成功率 予測ツール", headerStyle);
        EditorGUILayout.LabelField("基本設定", EditorStyles.boldLabel);
        referenceCamera = (Camera)EditorGUILayout.ObjectField("基準カメラ", referenceCamera, typeof(Camera), true);
        
        // [追加] 色選択のドロップダウンを追加
        selectedColor = (LabelColor)EditorGUILayout.EnumPopup("ラベル色", selectedColor);

        shapeSelection = (Shape)EditorGUILayout.EnumPopup("ターゲット形状", shapeSelection);
        EditorGUILayout.Space();

        // ターゲット形状がRectangleの場合、座標系をWorldSpaceに固定し、関連UIを非表示にする
        if (shapeSelection == Shape.Rectangle)
        {
            // 内部的にパラメータをWorldSpaceモードに強制する
            coordSystem = CoordSystem.WorldSpace;
            useDistance = false;
            useMux = false;
            // Rectangle選択時は、座標系やポインターなどのオプションUIを表示しない
        }
        else // Circleの場合のみ、すべてのオプションを表示する
        {
            EditorGUILayout.LabelField("座標系と計算モデル", EditorStyles.boldLabel);
            coordSystem = (CoordSystem)EditorGUILayout.EnumPopup("座標系", coordSystem);

            if (coordSystem == CoordSystem.PointerBased)
            {
                pointerObject = (GameObject)EditorGUILayout.ObjectField("ポインター", pointerObject, typeof(GameObject), true);
            }

            EditorGUI.BeginDisabledGroup(coordSystem == CoordSystem.WorldSpace);
            useDistance = EditorGUILayout.Toggle("距離(A)を使用", useDistance);
            EditorGUI.EndDisabledGroup();
            useMux = EditorGUILayout.Toggle("muxを使用", useMux);
            //useMux = true;
        }

        EditorGUILayout.Space();
        EditorGUILayout.HelpBox("シーンビューで対象オブジェクトを選択して、各種パラメータと成功率を確認します。", MessageType.Info);
    }


    /// <summary>
    /// シーンビューのUIを毎フレーム描画する。メインの計算と描画はここで行う。
    /// </summary>
    private void OnSceneGUI(SceneView sceneView)
    {
        if (labelStyle == null) return;

        // シーンビューの背景色に合わせて、少し明るめの見やすい色に調整しています。
        switch (selectedColor)
        {
            case LabelColor.Red:
                labelStyle.normal.textColor = new Color(1f, 0.2f, 0.2f);
                break;
            case LabelColor.Green:
                labelStyle.normal.textColor = new Color(0.2f, 0.8f, 0.2f);
                break;
            case LabelColor.Blue:
                labelStyle.normal.textColor = new Color(0.3f, 0.6f, 1f);
                break;
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

        // 計算結果を格納する変数
        float W, H, r = 0;
        Vector3[] highlightCorners;

        // ===== 1. 見かけのサイズ(W, H)の計算 =====
        if (coordSystem == CoordSystem.PointerBased)
        {
            // --- PointerBasedモード：ターゲットの向きを考慮した、正確な投影サイズを計算 ---
            // 視線方向は常に「カメラ→ターゲット」
            Vector3 sizeViewDirection = (targetPos - cameraPos).normalized;
            List<Vector3> worldPoints = GetWorldPoints(selectedObject);
            if (worldPoints == null)
            {
                Handles.Label(targetPos, "ターゲットにRendererまたはRectTransformがありません", labelStyle);
                return;
            }

            // 頂点を投影し、見かけの幅・高さを計算。描画用の枠線も受け取る。
            highlightCorners = CalculateProjectedSize(worldPoints, targetPos, sizeViewDirection, cameraTransform.up, out float projectedWidth, out float projectedHeight);

            // ワールドサイズを角度サイズに変換
            if (shapeSelection == Shape.Rectangle)
            {
                W = 2 * Mathf.Atan2(projectedWidth / 2f, z) * Mathf.Rad2Deg;
                H = 2 * Mathf.Atan2(projectedHeight / 2f, z) * Mathf.Rad2Deg;
            }
            else // Circle
            {
                float diameter = Mathf.Min(projectedWidth, projectedHeight);
                W = H = 2 * Mathf.Atan2(diameter / 2f, z) * Mathf.Rad2Deg;
                r = diameter / 2.0f;
            }
        }
        else // WorldSpaceモード
        {
            // --- WorldSpaceモード：従来通り、オブジェクトの向きを考慮しない純粋なサイズで計算 ---
            RectTransform rectTransform = selectedObject.GetComponent<RectTransform>();
            if (rectTransform != null) // UIオブジェクトの場合
            {
                float worldWidth = rectTransform.rect.width * rectTransform.lossyScale.x;
                float worldHeight = rectTransform.rect.height * rectTransform.lossyScale.y;
                W = 2 * Mathf.Atan2(worldWidth / 2f, z) * Mathf.Rad2Deg;
                H = 2 * Mathf.Atan2(worldHeight / 2f, z) * Mathf.Rad2Deg;
                if (shapeSelection == Shape.Circle) W = H = Mathf.Min(W, H);

                // 描画用にUIの四隅を取得
                highlightCorners = new Vector3[4];
                rectTransform.GetWorldCorners(highlightCorners);
            }
            else // 3Dオブジェクトの場合
            {
                Renderer targetRenderer = selectedObject.GetComponent<Renderer>();
                if (targetRenderer == null) { return; }
                Bounds bounds = targetRenderer.bounds;
                W = 2 * Mathf.Atan2(bounds.size.x, 2 * z) * Mathf.Rad2Deg;
                H = 2 * Mathf.Atan2(bounds.size.y, 2 * z) * Mathf.Rad2Deg;
                if (shapeSelection == Shape.Circle) W = H = Mathf.Min(W, H);

                // 描画用にカメラに正対する矩形を計算
                Vector3 up = cameraTransform.up * bounds.extents.y;
                Vector3 right = cameraTransform.right * bounds.extents.x;
                highlightCorners = new Vector3[] {
                    targetPos - right + up, targetPos + right + up,
                    targetPos + right - up, targetPos - right - up
                };
            }
        }

        // ===== 2. 成功率モデルのパラメータ(A, オフセットなど)を算出 =====
        float A = 0;

        if (coordSystem == CoordSystem.PointerBased)
        {
            // PointerBasedモードでは、「カメラ→ポインター」を視線の基準(newForward)とする
            Vector3 newForward = (pointerObject.transform.position - cameraPos).normalized;
            Vector3 targetDir = (targetPos - cameraPos).normalized;
            A = Vector3.Angle(newForward, targetDir);

        }
        else // WorldSpaceモード
        {
            useDistance = false;
        }

        // ===== 3. 回帰式を用いて各種パラメータを決定 =====
        double mux = useMux ? (g * W + h) : 0;
        double sigma_x, sigma_y;

        if (coordSystem == CoordSystem.WorldSpace)
        {
            sigma_x = 0.1660 + 0.1011 * W;
            sigma_y = 0.2481 + 0.1036 * (shapeSelection == Shape.Rectangle ? H : W);
        }
        else // PointerBasedモード
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

        // ===== 4. 最終的な成功率を計算 =====
        double successRate = ComputeSuccessRate(W, H, shapeSelection, mux, sigma_x, sigma_y);

        // ===== 5. シーンビューへの描画 =====
        DrawHighlights(targetPos, highlightCorners, r, shapeSelection, (targetPos - cameraPos).normalized);
        DrawInfoText(targetPos, cameraPos, W, H, A, successRate);
    }

    /// <summary>
    /// オブジェクトのワールド座標での頂点リストを取得するヘルパーメソッド。
    /// </summary>
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

    /// <summary>
    /// ワールド頂点群を、指定した視線に垂直な平面に投影し、その投影された形状の幅と高さを計算する。
    /// 同時に、描画用のハイライト枠の4隅の座標も返す。
    /// </summary>
    private Vector3[] CalculateProjectedSize(List<Vector3> worldPoints, Vector3 center, Vector3 viewDirection, Vector3 worldUp, out float width, out float height)
    {
        Vector3 projUp = Vector3.ProjectOnPlane(worldUp, viewDirection).normalized;
        Vector3 projRight = Vector3.Cross(projUp, viewDirection);

        float minX = float.MaxValue, maxX = float.MinValue;
        float minY = float.MaxValue, maxY = float.MinValue;

        foreach (Vector3 point in worldPoints)
        {
            Vector3 vecFromCenter = point - center;
            float px = Vector3.Dot(vecFromCenter, projRight);
            float py = Vector3.Dot(vecFromCenter, projUp);
            if (px < minX) minX = px;
            if (px > maxX) maxX = px;
            if (py < minY) minY = py;
            if (py > maxY) maxY = py;
        }

        width = maxX - minX;
        height = maxY - minY;

        Vector3 c0 = center + projRight * minX + projUp * minY;
        Vector3 c1 = center + projRight * minX + projUp * maxY;
        Vector3 c2 = center + projRight * maxX + projUp * maxY;
        Vector3 c3 = center + projRight * maxX + projUp * minY;
        return new Vector3[] { c0, c1, c2, c3 };
    }

    /// <summary>
    /// シーンビューにターゲットのハイライトを描画する。
    /// </summary>
    void DrawHighlights(Vector3 targetPos, Vector3[] corners, float circleRadius, Shape shape, Vector3 viewNormal)
    {
        Handles.color = labelStyle.normal.textColor;
        if (shape == Shape.Rectangle)
        {
            Handles.DrawPolyLine(corners[0], corners[1], corners[2], corners[3], corners[0]);
        }
        else // Circle
        {
            Handles.DrawWireDisc(targetPos, viewNormal, circleRadius);
        }
    }

    /// <summary>
    /// シーンビューに計算結果のテキスト情報を描画する。
    /// </summary>
    void DrawInfoText(Vector3 targetPos, Vector3 cameraPos, float W, float H, float A, double successRate)
    {
        string sizeText = shapeSelection == Shape.Rectangle ? $"W: {W:F2}°, H: {H:F2}°" : $"サイズ(W): {W:F2}°";
        string distText = (coordSystem == CoordSystem.PointerBased && useDistance) ? $"角度距離(A): {A:F2}°\n" : "";

        string infoText = $"見かけサイズ: {sizeText}\n" +
                          $"{distText}" +
                          $"成功率: {successRate:F2}%";

        Vector3 textPos = targetPos + (targetPos - cameraPos).normalized;
        Handles.Label(textPos, infoText, labelStyle);
    }

    /// <summary>
    /// パラメータを基に成功率を計算する (数値二重積分)。
    /// </summary>
    private double ComputeSuccessRate(float W, float H, Shape shape, double mux, double sigma_x, double sigma_y)
    {
        if (sigma_x < 1e-6) sigma_x = 1e-6;
        if (sigma_y < 1e-6) sigma_y = 1e-6;

        double totalProbability = 0;
        int steps = 100;

        float x_min = -W / 2.0f;
        float y_min = -H / 2.0f;
        float stepSizeX = W / steps;
        float stepSizeY = H / steps;
        double areaElement = stepSizeX * stepSizeY;

        for (int i = 0; i < steps; i++)
        {
            double x = x_min + (i + 0.5) * stepSizeX;
            for (int j = 0; j < steps; j++)
            {
                double y = y_min + (j + 0.5) * stepSizeY;
                bool inArea = false;
                if (shape == Shape.Rectangle)
                {
                    inArea = true;
                }
                else
                {
                    if (Math.Pow(x, 2) + Math.Pow(y, 2) <= Math.Pow(W / 2.0, 2))
                    {
                        inArea = true;
                    }
                }
                if (inArea)
                {
                    double exp_term = Math.Exp(
                        -(Math.Pow(x - mux, 2) / (2 * sigma_x * sigma_x))
                        - (Math.Pow(y, 2) / (2 * sigma_y * sigma_y))
                    );
                    double pdf = (1.0 / (2 * Math.PI * sigma_x * sigma_y)) * exp_term;
                    totalProbability += pdf * areaElement;
                }
            }
        }
        return totalProbability * 100.0;
    }
}