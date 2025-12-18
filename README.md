# Selection Success Rate Visualizer

[English](#english) | [日本語](#japanese)

---

<a name="english"></a>
# Selection Success Rate Visualizer (English)

This is a custom editor window tool for Unity that calculates and visualizes the "Selection Success Rate" in real-time when 3D objects or UI elements are viewed from a specific camera.

![Figure 1: Running in Unity](https://i.gyazo.com/90cfe030c8ed7422c4112d6e3ef3cb34.png)
*Fig 1: Execution image in Unity Editor*

## Overview

Based on mathematical models, this tool calculates the apparent size of a target object and the probability that a user can successfully select (click/tap) it. 

It is useful for usability evaluation in VR/AR content and 3D UIs as a guideline before actual device testing.

![Figure 2: Operation example](https://i.gyazo.com/29a60299b5c25fbae117a2fee5ca1216.png)
*Fig 2: Operation example*

## Installation

Install via the Unity Package Manager (UPM).

1. Open `Window > Package Manager` from the Unity menu bar.
2. Click the `+` button in the top left and select `Add package from git URL...`.
3. Enter the following URL and click `Add`:
   `https://github.com/Fvkuinu/RayCastSuccessPredictor.git`

## Settings

By configuring each item in the window, you can calculate the success rate according to the specific situation.

### 1. Basic Settings

| Item | Description |
| :--- | :--- |
| **Reference Camera** | Specify the camera that serves as the viewpoint (e.g., Player Camera, VR Headset). |
| **Target Shape** | Select the geometric model of the target. |

#### About Target Shapes
* **Circle:** The smaller of the apparent width and height is treated as the diameter.
* **Rectangle:** Uses the apparent width and height as is.
  * > **Note:** If this is selected, the coordinate system is fixed to `WorldSpace` (No pointer, No distance) because the movement direction `x` cannot be defined relative to the target.

### 2. Coordinate System & Model

Select the underlying model for calculation. This is the most important setting.

#### ■ WorldSpace
A simple and fast mode that does not consider the object's orientation.
* **Behavior:** Uses the bounding box size (from `Renderer.bounds` or `RectTransform.rect`) facing the camera.
* **Usage:** Suitable for rough size checks or evaluating billboard objects that always face the camera.

#### ■ PointerBased
A high-precision mode that accounts for the target's "tilt" relative to the camera.
* **Behavior:** Projects the target's vertices onto a plane perpendicular to the line of sight. It accurately reflects foreshortening (the effect of objects looking smaller when viewed at an angle).
* **Usage:** Suitable for accurate evaluation in VR/AR where users view objects from various angles.

### 3. Advanced Options (PointerBased Only)

#### Pointer
Set an object (e.g., an empty GameObject) that represents where the user's gaze or controller is "actually pointing."
* The angular offset between this pointer and the target is used as parameter `A`.

#### Use Distance (A)
* **ON:** Considers angular distance `A` (the angle between "Camera-to-Pointer" and "Camera-to-Target").
* **OFF:** Ignores angular distance.

#### Use mux
* **ON:** Adds a horizontal bias `mux` to the pointing hit distribution.

---

<a name="japanese"></a>
# Selection Success Rate Visualizer (日本語)

Unityエディタ上で3DオブジェクトやUI要素を特定のカメラから見た際の「選択成功率」をリアルタイムで計算・可視化するカスタムエディタウィンドウツールです。

## 概要

このツールは、ターゲットとなるオブジェクトが見かけ上どの程度の大きさに見えているか、またユーザーがどの程度の確率でそれを選択（クリック/タップ）できるかを数理モデルに基づいて算出します。

VR/ARコンテンツや3D UIのユーザビリティ評価において、実機テスト前の指針として活用できます。

## インストール方法

Unity Package Manager を使用してインストールします。

1. Unity メニューバーの `Window > Package Manager` を開きます。
2. 左上の `+` ボタンを押し、`Add package from git URL...` を選択します。
3. 以下のURLを入力して `Add` を押してください。
   `https://github.com/Fvkuinu/RayCastSuccessPredictor.git`

## 設定項目

### 1. 基本設定 (Basic Settings)

| 項目名 | 説明 |
| :--- | :--- |
| **基準カメラ** | 計算の視点となるカメラを指定します。 |
| **ターゲット形状** | ターゲットの形状モデルを選択します。 |

#### ターゲット形状について
* **Circle (円形):** 見かけの幅と高さのうち、小さい方を直径として扱います。
* **Rectangle (矩形):** 見かけの幅と高さをそのまま使用します。

### 2. 座標系と計算モデル (Coordinate System)

#### ■ WorldSpace
オブジェクトの向きを考慮しない、シンプルで高速な計算モードです。常にカメラに正対しているオブジェクトの評価に適しています。

#### ■ PointerBased
カメラから見たターゲットの「傾き」を考慮する、高精度なモードです。斜めから見たターゲットが小さく見える効果（射影短縮）が正確に反映されます。

### 3. 詳細オプション (PointerBasedモードのみ)

* **ポインター (Pointer):** ユーザーの視線やコントローラーが指し示している場所を示すオブジェクトを設定します。
* **距離(A)を使用:** 「カメラ→ポインター」と「カメラ→ターゲット」のなす角度を計算に考慮します。
* **muxを使用:** ポインティングの命中分布に水平方向のバイアスを加えます。