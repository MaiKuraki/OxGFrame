﻿using Cysharp.Threading.Tasks;
using HybridCLR;
using OxGFrame.AssetLoader;
using OxGFrame.AssetLoader.Bundle;
using OxGFrame.Hotfixer.HotfixEvent;
using OxGKit.LoggingSystem;
using System;
using System.Reflection;
using UniFramework.Machine;
using UnityEngine;
using YooAsset;

namespace OxGFrame.Hotfixer.HotfixFsm
{
    public static class HotfixFsmStates
    {
        /// <summary>
        /// 1. 流程準備工作
        /// </summary>
        public class FsmHotfixPrepare : IStateNode
        {
            private StateMachine _machine;

            public FsmHotfixPrepare() { }

            void IStateNode.OnCreate(StateMachine machine)
            {
                this._machine = machine;
            }

            void IStateNode.OnEnter()
            {
                HotfixEvents.HotfixFsmState.SendEventMessage(this);
                Logging.Print<Logger>("(Powered by HybridCLR) Hotfix is now running...");
                this._machine.ChangeState<FsmInitHotfixPackage>();
            }

            void IStateNode.OnUpdate()
            {
            }

            void IStateNode.OnExit()
            {
            }
        }

        /// <summary>
        /// 2. 初始 Hotfix Package
        /// </summary>
        public class FsmInitHotfixPackage : IStateNode
        {
            private StateMachine _machine;

            public FsmInitHotfixPackage() { }

            void IStateNode.OnCreate(StateMachine machine)
            {
                this._machine = machine;
            }

            void IStateNode.OnEnter()
            {
                HotfixEvents.HotfixFsmState.SendEventMessage(this);
                this._InitHotfixPackage().Forget();
            }

            void IStateNode.OnUpdate()
            {
            }

            void IStateNode.OnExit()
            {
            }

            private async UniTask _InitHotfixPackage()
            {
                PackageInfoWithBuild packageInfoWithBuild = HotfixManager.GetInstance().packageInfoWithBuild;
                if (packageInfoWithBuild == null)
                {
                    packageInfoWithBuild = new AppPackageInfoWithBuild()
                    {
                        buildMode = BundleConfig.BuildMode.ScriptableBuildPipeline,
                        packageName = HotfixManager.GetInstance().packageName
                    };
                }
                bool isInitialized = await AssetPatcher.InitPackage(packageInfoWithBuild);
                if (isInitialized) this._machine.ChangeState<FsmUpdateHotfixPackage>();
                else HotfixEvents.HotfixInitFailed.SendEventMessage();
            }
        }

        /// <summary>
        /// 3. 更新 Hotfix Package
        /// </summary>
        public class FsmUpdateHotfixPackage : IStateNode
        {
            private StateMachine _machine;

            public FsmUpdateHotfixPackage() { }

            void IStateNode.OnCreate(StateMachine machine)
            {
                this._machine = machine;
            }

            void IStateNode.OnEnter()
            {
                HotfixEvents.HotfixFsmState.SendEventMessage(this);
                this._UpdateHotfixPackage().Forget();
            }

            void IStateNode.OnUpdate()
            {
            }

            void IStateNode.OnExit()
            {
            }

            private async UniTask _UpdateHotfixPackage()
            {
                bool isUpdated = await AssetPatcher.UpdatePackage(HotfixManager.GetInstance().packageName);
                if (isUpdated) this._machine.ChangeState<FsmHotfixCreateDownloader>();
                else HotfixEvents.HotfixUpdateFailed.SendEventMessage();
            }
        }

        /// <summary>
        /// 4. 創建 Hotfix 下載器
        /// </summary>
        public class FsmHotfixCreateDownloader : IStateNode
        {
            private StateMachine _machine;

            public FsmHotfixCreateDownloader() { }

            void IStateNode.OnCreate(StateMachine machine)
            {
                this._machine = machine;
            }

            void IStateNode.OnEnter()
            {
                HotfixEvents.HotfixFsmState.SendEventMessage(this);
                this._CreateHotfixDownloader();
            }

            void IStateNode.OnUpdate()
            {
            }

            void IStateNode.OnExit()
            {
            }

            private void _CreateHotfixDownloader()
            {
                // Get hotfix package
                var package = AssetPatcher.GetPackage(HotfixManager.GetInstance().packageName);

                // Create a downloader
                HotfixManager.GetInstance().mainDownloader = AssetPatcher.GetPackageDownloader(package);
                int totalDownloadCount = HotfixManager.GetInstance().mainDownloader.TotalDownloadCount;

                // Do begin download, if download count > 0
                if (totalDownloadCount > 0) this._machine.ChangeState<FsmHotfixBeginDownload>();
                else this._machine.ChangeState<FsmHotfixDownloadOver>();
            }
        }

        /// <summary>
        /// 5. 開始下載 Hotfix files
        /// </summary>
        public class FsmHotfixBeginDownload : IStateNode
        {
            private StateMachine _machine;

            public FsmHotfixBeginDownload() { }

            void IStateNode.OnCreate(StateMachine machine)
            {
                this._machine = machine;
            }

            void IStateNode.OnEnter()
            {
                HotfixEvents.HotfixFsmState.SendEventMessage(this);
                this._StartDownloadHotfix().Forget();
            }

            void IStateNode.OnUpdate()
            {
            }

            void IStateNode.OnExit()
            {
            }

            private async UniTask _StartDownloadHotfix()
            {
                // Get hotfix package
                var package = AssetPatcher.GetPackage(HotfixManager.GetInstance().packageName);

                // Get main downloader
                var downloader = HotfixManager.GetInstance().mainDownloader;
                downloader.DownloadErrorCallback = (DownloadErrorData data) =>
                {
                    HotfixEvents.HotfixDownloadFailed.SendEventMessage(data.FileName, data.ErrorInfo);
                };
                downloader.BeginDownload();

                await downloader;

                if (downloader.Status != EOperationStatus.Succeed) return;

                this._machine.ChangeState<FsmHotfixDownloadOver>();
            }
        }

        /// <summary>
        /// 6. 完成 Hotfix 下載
        /// </summary>
        public class FsmHotfixDownloadOver : IStateNode
        {
            private StateMachine _machine;

            public FsmHotfixDownloadOver() { }

            void IStateNode.OnCreate(StateMachine machine)
            {
                this._machine = machine;
            }

            void IStateNode.OnEnter()
            {
                HotfixEvents.HotfixFsmState.SendEventMessage(this);
                this._machine.ChangeState<FsmHotfixClearCache>();
            }

            void IStateNode.OnUpdate()
            {
            }

            void IStateNode.OnExit()
            {
                HotfixManager.GetInstance().ReleaseMainDownloader();
            }
        }

        /// <summary>
        /// 7. 清理未使用的緩存文件
        /// </summary>
        public class FsmHotfixClearCache : IStateNode
        {
            private StateMachine _machine;

            public FsmHotfixClearCache() { }

            void IStateNode.OnCreate(StateMachine machine)
            {
                this._machine = machine;
            }

            void IStateNode.OnEnter()
            {
                HotfixEvents.HotfixFsmState.SendEventMessage(this);
                this._ClearUnusedCache().Forget();
            }

            void IStateNode.OnUpdate()
            {
            }

            void IStateNode.OnExit()
            {
            }

            private async UniTask _ClearUnusedCache()
            {
                // Get hotfix package
                var package = AssetPatcher.GetPackage(HotfixManager.GetInstance().packageName);
                var clearUnusedBundleFilesOperation = package.ClearCacheFilesAsync(EFileClearMode.ClearUnusedBundleFiles);
                var clearUnusedManifestFilesOperation = package.ClearCacheFilesAsync(EFileClearMode.ClearUnusedManifestFiles);
                await clearUnusedBundleFilesOperation;
                await clearUnusedManifestFilesOperation;

                // Start load hotfix assemblies
                if (clearUnusedBundleFilesOperation.IsDone &&
                    clearUnusedManifestFilesOperation.IsDone)
                {
                    if (HotfixManager.GetInstance().IsDisabled())
                    {
                        Logging.PrintWarning<Logger>("[DISABLED] Skip processing AOTAssemblies.");
                        this._machine.ChangeState<FsmLoadHotfixAssemblies>();
                    }
                    else
                    {
                        this._machine.ChangeState<FsmLoadAOTAssemblies>();
                    }
                }
            }
        }

        /// <summary>
        /// 8. 開始補充 AOT 元數據
        /// </summary>
        public class FsmLoadAOTAssemblies : IStateNode
        {
            private StateMachine _machine;

            public FsmLoadAOTAssemblies() { }

            void IStateNode.OnCreate(StateMachine machine)
            {
                this._machine = machine;
            }

            void IStateNode.OnEnter()
            {
                HotfixEvents.HotfixFsmState.SendEventMessage(this);
                this._LoadAOTAssemblies().Forget();
            }

            void IStateNode.OnUpdate()
            {
            }

            void IStateNode.OnExit()
            {
            }

            private async UniTask _LoadAOTAssemblies()
            {
                if (BundleConfig.playMode == BundleConfig.PlayMode.EditorSimulateMode)
                {
                    this._machine.ChangeState<FsmLoadHotfixAssemblies>();
                    return;
                }

                string[] aotMetaAssemblyFiles = HotfixManager.GetInstance().GetAOTAssemblyNames();

                try
                {
                    if (aotMetaAssemblyFiles != null)
                    {
                        // 注意, 補充元數據是給 AOT dll 補充元數據, 而不是給熱更新 dll 補充元數據
                        // 熱更新 dll 不缺元數據, 不需要補充, 如果調用 LoadMetadataForAOTAssembly 會返回錯誤
                        HomologousImageMode mode = HomologousImageMode.SuperSet;
                        foreach (var dllName in aotMetaAssemblyFiles)
                        {
                            var dll = await AssetLoaders.LoadAssetAsync<TextAsset>(HotfixManager.GetInstance().packageName, dllName);
                            if (dll != null)
                            {
                                // Get bytes 只能在 main thread 進行 (Unity TextAsset 的坑)
                                byte[] binary = dll.bytes;
#if !UNITY_WEBGL
                                // 切換至其他線程
                                await UniTask.SwitchToThreadPool();
#endif
                                // 加載 assembly 對應的 dll, 會自動為它 hook, 一旦 aot 泛型函數的 native 函數不存在, 用解釋器版本代碼
                                LoadImageErrorCode err = RuntimeApi.LoadMetadataForAOTAssembly(binary, mode);
#if !UNITY_WEBGL
                                // 切回至主線程
                                await UniTask.SwitchToMainThread();
#endif
                                // Unload after load
                                AssetLoaders.UnloadAsset(dllName);
                                Logging.Print<Logger>($"Loaded AOT Assembly: {dllName}, mode: {mode}, ret: {err}");
                            }
                            else
                            {
                                Logging.PrintError<Logger>($"Failed to load AOT Assembly: {dllName}, mode: {mode}");
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    Logging.PrintException<Logger>(ex);
                }

                this._machine.ChangeState<FsmLoadHotfixAssemblies>();
            }
        }

        /// <summary>
        /// 9. 開始加載 Hotfix 元數據
        /// </summary>
        public class FsmLoadHotfixAssemblies : IStateNode
        {
            private StateMachine _machine;

            public FsmLoadHotfixAssemblies() { }

            void IStateNode.OnCreate(StateMachine machine)
            {
                this._machine = machine;
            }

            void IStateNode.OnEnter()
            {
                HotfixEvents.HotfixFsmState.SendEventMessage(this);
                this._LoadHotfixAssemblies().Forget();
            }

            void IStateNode.OnUpdate()
            {
            }

            void IStateNode.OnExit()
            {
            }

            private async UniTask _LoadHotfixAssemblies()
            {
                string[] hotfixAssemblyFiles = HotfixManager.GetInstance().GetHotfixAssemblyNames();

                try
                {
                    if (hotfixAssemblyFiles != null)
                    {
                        foreach (var dllName in hotfixAssemblyFiles)
                        {
                            Assembly hotfixAsm = null;
                            if (Application.isEditor ||
                                BundleConfig.playMode == BundleConfig.PlayMode.EditorSimulateMode ||
                                HotfixManager.GetInstance().IsDisabled())
                            {
                                // 移除 .dll 擴展名
                                var fileExtension = ".dll";
                                var newLength = dllName.Length - fileExtension.Length;
                                var newDllName = dllName.Substring(0, newLength);
                                // 直接查找獲取 Hotfix 程序集
                                foreach (var asm in System.AppDomain.CurrentDomain.GetAssemblies())
                                {
                                    if (asm.GetName().Name == newDllName)
                                    {
                                        hotfixAsm = asm;
                                        break;
                                    }
                                }
                            }
                            else
                            {
                                var dll = await AssetLoaders.LoadAssetAsync<TextAsset>(HotfixManager.GetInstance().packageName, dllName);
                                if (dll != null)
                                {
                                    // Get bytes 只能在 main thread 進行 (Unity TextAsset 的坑)
                                    byte[] binary = dll.bytes;
#if !UNITY_WEBGL
                                    // 切換至其他線程
                                    await UniTask.SwitchToThreadPool();
#endif
                                    // 加載熱更 dlls
                                    hotfixAsm = Assembly.Load(binary);
#if !UNITY_WEBGL
                                    // 切回至主線程
                                    await UniTask.SwitchToMainThread();
#endif
                                    // Unload after load
                                    AssetLoaders.UnloadAsset(dllName);
                                }
                            }

                            if (hotfixAsm != null)
                            {
                                HotfixManager.GetInstance().AddHotfixAssembly(dllName, hotfixAsm);
                                Logging.Print<Logger>($"Loaded Hotfix Assembly: {dllName}");
                            }
                            else
                            {
                                Logging.PrintError<Logger>($"Failed to load Hotfix Assembly: {dllName}");
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    Logging.PrintException<Logger>(ex);
                }

                this._machine.ChangeState<FsmHotfixDone>();
            }
        }

        /// <summary>
        /// 10. 完成 Hotfix
        /// </summary>
        public class FsmHotfixDone : IStateNode
        {
            public FsmHotfixDone() { }

            void IStateNode.OnCreate(StateMachine machine)
            {
            }

            void IStateNode.OnEnter()
            {
                HotfixEvents.HotfixFsmState.SendEventMessage(this);
                HotfixManager.GetInstance().MarkAsDone();
                Logging.PrintInfo<Logger>("(Powered by HybridCLR) Hotfix all done.");
            }

            void IStateNode.OnUpdate()
            {
            }

            void IStateNode.OnExit()
            {
            }
        }
    }
}
