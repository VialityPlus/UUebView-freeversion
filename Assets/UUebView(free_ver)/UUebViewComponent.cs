using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace UUebView {
    /**
        UUebView component.

        testing usage:
            attach this component to gameobject and set preset urls and event receiver.

        actual usage:
            let's use UUebView.GenerateSingleViewFromHTML or UUebView.GenerateSingleViewFromUrl.
            they returns view GameObject of UUebView and attach it to your window.
     */
    public class UUebViewComponent : MonoBehaviour, IUUebView {
        /*
            preset parameters.
            you can use UUebView with preset paramters for testing.
         */
        public string presetUrl;
        public GameObject presetEventReceiver;


        public UUebViewCore Core {
            get; private set;
        }

        void Start () {
            /*
                if preset parameters exists, UUebView shows preset view on this gameObject.
                this feature is for testing.
             */
            if (!string.IsNullOrEmpty(presetUrl)) {
                Debug.Log("show preset view.");
                var viewObj = this.gameObject;
                
                var uuebView = viewObj.GetComponent<UUebViewComponent>();
                var uuebViewCore = new UUebViewCore(uuebView);
                uuebView.SetCore(uuebViewCore);
                uuebViewCore.DownloadHtml(presetUrl, GetComponent<RectTransform>().sizeDelta, presetEventReceiver);
            }
        }

        public static GameObject GenerateSingleViewFromHTML(
            GameObject eventReceiverGameObj, 
            string source, 
            Vector2 viewRect, 
            ResourceLoader.MyHttpRequestHeaderDelegate requestHeader=null,
            ResourceLoader.MyHttpResponseHandlingDelegate httpResponseHandlingDelegate=null,
            string viewName=ConstSettings.ROOTVIEW_NAME
        ) {
            var viewObj = new GameObject("UUebView");
            viewObj.AddComponent<RectTransform>();
            viewObj.name = viewName;

            var uuebView = viewObj.AddComponent<UUebViewComponent>();
            var uuebViewCore = new UUebViewCore(uuebView, requestHeader, httpResponseHandlingDelegate);
            uuebView.SetCore(uuebViewCore);
            uuebViewCore.LoadHtml(source, viewRect, eventReceiverGameObj);

            return viewObj;
        }

        public static GameObject GenerateSingleViewFromUrl(
            GameObject eventReceiverGameObj, 
            string url, 
            Vector2 viewRect, 
            ResourceLoader.MyHttpRequestHeaderDelegate requestHeader=null,
            ResourceLoader.MyHttpResponseHandlingDelegate httpResponseHandlingDelegate=null,
            string viewName=ConstSettings.ROOTVIEW_NAME
        ) {
            var viewObj = new GameObject("UUebView");
            viewObj.AddComponent<RectTransform>();
            viewObj.name = viewName;
            
            var uuebView = viewObj.AddComponent<UUebViewComponent>();
            var uuebViewCore = new UUebViewCore(uuebView, requestHeader, httpResponseHandlingDelegate);
            uuebView.SetCore(uuebViewCore);
            uuebViewCore.DownloadHtml(url, viewRect, eventReceiverGameObj);

            return viewObj;
        }

        public void SetCore (UUebViewCore core) {
            this.Core = core;
        }

        void Update () {
            Core.Dequeue(this);
        }

        public void EmitButtonEventById (string elementId) {
            Core.OnImageTapped(elementId);
        }

        public void EmitLinkEventById (string elementId) {
            Core.OnLinkTapped(elementId);
        }

        void IUUebView.AddChild (Transform transform) {
            transform.SetParent(this.transform);
        }

        void IUUebView.UpdateSize (Vector2 size) {
            var parentRectTrans = this.transform.parent.GetComponent<RectTransform>();
            parentRectTrans.sizeDelta = size;
        }

        GameObject IUUebView.GetGameObject () {
            return this.gameObject;
        }

        void IUUebView.StartCoroutine (IEnumerator iEnum) {
            this.StartCoroutine(iEnum);
        }
    }
}