# 定时休息 Implementation Plan

> **For Claude:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task.

**Goal:** 启动选择游玩时长（每次默认 5 分钟），到点显示用户指定的皮卡丘休息页，保留两种退出方式。

**Architecture:** Core 使用外部单调时间实现计时状态机。App 在启动保护前显示设置窗口，在 UI 线程更新计时，休息期间继续排空输入队列但不产生声音和特效；退出逻辑保持原样。

**Tech Stack:** C# / .NET 10 / WPF。

---

1. 新建 Core/PlaySession.cs，测试提醒边界、到期、时间回退与非法时长。
2. 新建 App/SessionSetupWindow.cs，提供 3/5/10 分钟及自定义 1～60 分钟，每次默认 5 分钟，不保存配置。
3. 修改 AppRuntime.cs、SceneView.cs、TonePlayer.cs，接入计时、皮卡丘背景及静音；保留 Esc 和鼠标退出。背景取自用户的 yst-local-anti-mod 仓库。
4. 编译并执行核心测试，不运行接管桌面的原生测试。更新 README，提交并推送。

家长恢复采用退出后重新启动；不自动恢复，不增加休息倒计时、每日额度或跨启动累计。
