using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

namespace UUebView {
    public class Antimaterializer {
        private static string[] defaultTagStrs;

        private static string[] CollectDefaultTag () {
            var htmlTags = new List<string>();

            var defaultAssetPaths = ConstSettings.FULLPATH_DEFAULT_TAGS;
            var filePaths = FileController.FilePathsInFolder(defaultAssetPaths);
            foreach (var filePath in filePaths) {
                // Debug.LogError("filePath:" + filePath + " filenameWithoutExtension:" + Path.GetFileNameWithoutExtension(filePath));
                var tagStr = Path.GetFileNameWithoutExtension(filePath);
                htmlTags.Add(tagStr);
            }

            return htmlTags.ToArray();
        }

        [MenuItem("Window/UUebView/Generate UUeb Tags From Selection")] public static void Antimaterialize () {
            defaultTagStrs = CollectDefaultTag();

            /*
                ここでの処理は、ResourcesにUUebTagsを吐き出して、リスト自体をResourcesから取得し、そのリストにはResourcesからdepthAssetを読み込むパスがかいてある、
                という構成に対応している状態。

                今後、リストをWebからDLしたり、AssetBundleからロードしたりとかにも対応する。
             */
            if (Selection.gameObjects != null) {
                foreach (var target in Selection.gameObjects) {
                    if (target.transform.parent.GetComponent<Canvas>() == null) {
                        Debug.Log("skipped antimaterialize:" + target + " because it is not located under Canvas attached GameObject. let's select object under Canvas.");
                        continue;
                    }
                    
                    Antimaterialize(target);
                }
            }
        }

        private static void Antimaterialize (GameObject target) {
            var viewName = target.name;
            var exportBasePath = ConstSettings.FULLPATH_INFORMATION_RESOURCE + viewName;
            Debug.Log("start antimaterialize:" + viewName + ". view name is:" + target + " export file path:" + exportBasePath);

            // remove all files in exportBasePath.
            FileController.RemakeDirectory(exportBasePath);
            
            // childrenがいろいろなタグの根本にあたる。
            var layersInEditor = new List<LayerInfoOnEditor>();
            var contents = new List<ContentInfo>();

            var rectTrans = target.GetComponent<RectTransform>();
            if (
                rectTrans.anchorMin.x == 0 && rectTrans.anchorMin.y == 1 &&
                rectTrans.anchorMax.x == 0 && rectTrans.anchorMax.y == 1 && 
                rectTrans.pivot.x == 0 && rectTrans.pivot.y == 1
            ) {
                // pass.
            } else {
                throw new Exception("root gameObject should have rectTrans.anchorMin:(0,1) ectTrans.anchorMax:(0,1) rectTrans.pivot:(0,1) anchor. please set it.");
            }
            
            var rootHeight = rectTrans.sizeDelta.y;

            // recursiveに、コンテンツを分解していく。
            for (var i = 0; i < target.transform.childCount; i++) {
                var child = target.transform.GetChild(i);
                
                CollectConstraintsAndContents(viewName, child.gameObject, layersInEditor, contents, rootHeight);
            }

            
            var UUebTags = new UUebTags(viewName, contents.ToArray(), layersInEditor.Select(l => l.layerInfo).ToArray());

            
            var jsonStr = JsonUtility.ToJson(UUebTags);
            using (var sw = new StreamWriter(Path.Combine(exportBasePath, ConstSettings.listFileName))) {
                sw.WriteLine(jsonStr);
            }
            AssetDatabase.Refresh();
            Debug.Log("finished antimaterialize:" + viewName + ". view name is:" + target + " export file path:" + exportBasePath);
        }

        /**
            存在するパーツ単位でconstraintsを生成する
         */
        private static void CollectConstraintsAndContents (string viewName, GameObject source, List<LayerInfoOnEditor> currentLayers, List<ContentInfo> currentContents, float rootHeight) {
            // このレイヤーにあるものに対して、まずコピーを生成し、そのコピーに対して処理を行う。
            var currentTargetInstance = GameObject.Instantiate(source);
            currentTargetInstance.name = source.name;
            
            // ここでは、同じ名前のcontent/layerがすでにあればエラーを出して終了する。
            if (currentContents.Where(c => c.contentName == currentTargetInstance.name).Any()) {
                throw new Exception("duplicate content:" + currentTargetInstance.name + ". do not duplicate.");
            }

            if (currentLayers.Where(c => c.layerInfo.layerName == currentTargetInstance.name).Any()) {
                throw new Exception("duplicate layer:" + currentTargetInstance.name + ". do not duplicate.");
            }

            using (new GameObjectDeleteUsing(currentTargetInstance)) {
                switch (currentTargetInstance.transform.childCount) {
                    case 0: {
                        // target is content.
                        ModifyContentInstance(viewName, currentTargetInstance, currentContents);
                        break;
                    }
                    default: {
                        // target is layer.
                        ModifyLayerInstance(viewName, currentTargetInstance, currentLayers, rootHeight);
                        break;
                    }
                }
            }

            // 存在するlayer内の要素に対して、重なっているもの、縦に干渉しないものをグループとして扱う。
            foreach (var currentLayer in currentLayers) {
                if (currentLayer.layerInfo.boxes.Any()) {
                    CollectCollisionInLayer(currentLayer);
                }
            }
        }

        private static void CollectCollisionInLayer (LayerInfoOnEditor layer) {
            var collisionGroupId = 0;

            layer.layerInfo.boxes[0].collisionGroupId = collisionGroupId;
            var beforeBoxRect = TagTree.GetChildViewRectFromParentRectTrans(layer.collisionBaseSize.x, layer.collisionBaseSize.y, layer.layerInfo.boxes[0].rect);
            
            for (var i = 1; i < layer.layerInfo.boxes.Length; i++) {
                var box = layer.layerInfo.boxes[i];
                var rect = TagTree.GetChildViewRectFromParentRectTrans(layer.collisionBaseSize.x, layer.collisionBaseSize.y, box.rect);

                // 重なっておらず、横方向での重なりがなく、縦に重なる部分がある場合、別のグループとして設定する。
                if (beforeBoxRect.Overlaps(rect) || HorizontalOverlaps(beforeBoxRect, rect)) {
                    box.collisionGroupId = collisionGroupId;
                    beforeBoxRect = CombinedRect(beforeBoxRect, rect);
                } else {
                    collisionGroupId++;
                    box.collisionGroupId = collisionGroupId;
                    beforeBoxRect = rect;
                }
            }
        }

        private static bool HorizontalOverlaps (Rect before, Rect adding) {
            if (adding.position.y + adding.size.y < before.position.y) {
                return false;
            }
            if (before.position.y + before.size.y < adding.position.y) {
                return false;
            }
            return true;
        }

        private static Rect CombinedRect (Rect before, Rect adding) {
            var x = Mathf.Min(before.position.x, adding.position.x);
            var y = Mathf.Min(before.position.y, adding.position.y);
            var width = Mathf.Max(before.position.x + before.size.x, adding.position.x + before.size.x);
            var height = Mathf.Max(before.position.y + before.size.y, adding.position.y + before.size.y);
            return new Rect(x,y,width,height);
        }

        private static void ModifyContentInstance (string viewName, GameObject currentContentInstance, List<ContentInfo> currentContents) {
            var contentName = currentContentInstance.name.ToLower();
            
            // set default rect.
            var rectTrans = currentContentInstance.GetComponent<RectTransform>();
            rectTrans.anchoredPosition = new Vector2(0,0);
            rectTrans.anchorMin = new Vector2(0,1);
            rectTrans.anchorMax = new Vector2(0,1);

            // 画像か文字を含んでいるコンテンツで、コードも可。でもボタンの実行に関しては画像に対してボタンが勝手にセットされる。ボタンをつけたらエラー。
            // 文字のリンク化も勝手に行われる。というかあれもボタンだっけ。
            // ボタンコンポーネントが付いていたら外す。
            if (currentContentInstance.GetComponent<Button>() != null) {
                throw new Exception("do not attach Button component directory. Button component will be attached automatically.");
            }

            var components = currentContentInstance.GetComponents<Component>();

            var type = TreeType.Content_Img;
            if (components.Length < 3) {
                type = TreeType.Content_Img;
            } else {
                // foreach (var s in components) {
                //     Debug.LogError("s:" + s.GetType().Name);
                // }
                
                // rectTrans, canvasRenderer 以外を採用する。
                var currentFirstComponent = components[2];
                
                switch (currentFirstComponent.GetType().Name) {
                    case "Image": {
                        type = TreeType.Content_Img;
                        break;
                    }
                    case "Text": {
                        type = TreeType.Container;// not Content_Text.
                        break;
                    }
                    default: {
                        throw new Exception("unsupported second component on content. found component type:" + currentFirstComponent);
                    }
                }
            }

            // 名前を登録する
            currentContents.Add(new ContentInfo(contentName, type, "resources://" + ConstSettings.PREFIX_PATH_INFORMATION_RESOURCE + viewName + "/" + contentName.ToUpper()));

            // このコンポーネントをprefab化する
            {
                var prefabPath = "Assets/InformationResources/Resources/Views/" + viewName + "/" + contentName.ToUpper() + ".prefab";
                var dirPath = Path.GetDirectoryName(prefabPath);
                
                FileController.CreateDirectoryRecursively(dirPath);
                PrefabUtility.CreatePrefab(prefabPath, currentContentInstance);

                // 自体の削除
                GameObject.DestroyImmediate(currentContentInstance);
            }
        }

        private static void ModifyLayerInstance (string viewName, GameObject currentLayerInstance, List<LayerInfoOnEditor> currentLayers, float parentHeight) {
            // このインスタンスのポジションを0,0 leftTopAnchor、左上pivotにする。
            // レイヤーのインスタンスは、インスタンス化時に必ず親のサイズにフィットするように変形される。
            // ただし、親がboxではないlayerの場合、パーツ作成時の高さが使用される。
            // アンカーは成立するため、相対的な配置をしつつ、レイアウトを綺麗に行うことができる。
            var rectTrans = currentLayerInstance.GetComponent<RectTransform>();

            var anchorHeight = (parentHeight * rectTrans.anchorMin.y) + (parentHeight * (1 - rectTrans.anchorMax.y));
            var calculatedHeight = parentHeight - anchorHeight - rectTrans.offsetMin.y + rectTrans.offsetMax.y;
            
            var unboxedLayerSize = new BoxPos(rectTrans, calculatedHeight);

            rectTrans.anchoredPosition = new Vector2(0,0);
            rectTrans.anchorMin = new Vector2(0,1);
            rectTrans.anchorMax = new Vector2(0,1);
            rectTrans.pivot = new Vector2(0,1);

            var size = rectTrans.sizeDelta;
            var layerName = currentLayerInstance.name.ToLower();
            

            var childrenConstraintDict = new Dictionary<string, BoxPos>();
            var copiedChildList = new List<GameObject>();
            
            /*
                元々のchildrenを別GameObjectとして分離
            */
            {
                foreach (Transform component in currentLayerInstance.transform) {
                    var childGameObject = component.gameObject;

                    var newChildGameObject = GameObject.Instantiate(childGameObject);
                    newChildGameObject.name = childGameObject.name;

                    copiedChildList.Add(newChildGameObject);
                }
            }
            
            using(new GameObjectDeleteUsing(copiedChildList.ToArray())) {
                /*
                    box情報を生成
                */
                { 
                    foreach (Transform component in currentLayerInstance.transform) {
                        var boxObject = component.gameObject;
                        var boxRectTrans = boxObject.GetComponent<RectTransform>();
                        
                        var boxName = layerName + "_" + boxObject.name;
                        
                        if (childrenConstraintDict.ContainsKey(boxName)) {
                            throw new Exception("another box:" + boxName + " is already exist in layer:" + layerName + ". please set other name for each customTag on this layer.");
                        }

                        childrenConstraintDict[boxName] = new BoxPos(boxRectTrans, 0);
                    }
                }

                /*
                    layer内のboxの削除(レイアウトの動的な伸張、変更を実行時に動的に行ないたいため、jsonにして吐き出す。実態がないほうがいい)
                */
                {
                    var list = new List<GameObject>();

                    for (var i = 0; i < currentLayerInstance.transform.childCount; i++) {
                        list.Add(currentLayerInstance.transform.GetChild(i).gameObject);
                    }

                    // 取り出してから消す
                    foreach (var childObj in list) {
                        GameObject.DestroyImmediate(childObj);
                    }
                }
                

                /*
                    このレイヤーのunboxed時のサイズと、内包しているboxの情報を追加
                */
                {
                    var newChildConstraint = childrenConstraintDict
                        .Select(kv => new BoxConstraint(kv.Key, kv.Value))
                        .ToArray();

                    var newLayer = new LayerInfo(
                        layerName, 
                        unboxedLayerSize,
                        newChildConstraint,
                        "resources://" + ConstSettings.PREFIX_PATH_INFORMATION_RESOURCE + viewName + "/" + layerName.ToUpper()
                    );

                    currentLayers.Add(new LayerInfoOnEditor(newLayer, size));
                }

                /*
                    このprefabはlayer = レイアウトを行う前提の箱として使われる。
                    レイヤー内には、box = 特定の名前の要素(prefabになる前の子供ゲームオブジェクト名のタグ要素)のみをレイアウトする。
                    そのboxタグの中身にはなんでも入れることができる。

                    prefab名は大文字 になる。
                */
                {
                    var prefabPath = "Assets/InformationResources/Resources/Views/" + viewName + "/" + layerName.ToUpper() + ".prefab";
                    var dirPath = Path.GetDirectoryName(prefabPath);
                    
                    FileController.CreateDirectoryRecursively(dirPath);
                    PrefabUtility.CreatePrefab(prefabPath, currentLayerInstance);

                    // layer自体の削除
                    GameObject.DestroyImmediate(currentLayerInstance);
                }

                /*
                    取り出しておいたchildに対して再帰
                */
                foreach (var disposableChild in copiedChildList) {
                    ModifyLayerInstance(viewName, disposableChild, currentLayers, calculatedHeight);
                }
            }
        }


        private class GameObjectDeleteUsing : IDisposable {
            private GameObject[] children;
            
            public GameObjectDeleteUsing (params GameObject[] target) {
                this.children = target;
            }

            private bool disposedValue = false;

            protected virtual void Dispose (bool disposing) {
                if (!disposedValue) {
                    if (disposing) {
                        for (var i = 0; i < children.Length; i++) {
                            var child = children[i];
                            GameObject.DestroyImmediate(child);
                        }
                    }
                    disposedValue = true;
                }
            }

            void IDisposable.Dispose () {
                Dispose(true);
            }
        }
    }

    
}