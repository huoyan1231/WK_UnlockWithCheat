# WK_UnlockWithCheat

> 作弊模式下也能解锁新内容，且不会污染排行榜与你的真实 Steam 成就。

`WK_UnlockWithCheat` 是一个 [BepInEx](https://github.com/BepInEx/BepInEx) 模组，用于 **White Knuckle**（白色指节）。
它做两件事：

1. **作弊时仍然可以解锁新内容**（成就 / 进度 / 经验）并让进度正常保存；
2. **通过共享库 `WK_huoyan1231COMLib` 拦截外部上传**，在模组载入时即禁用排行榜成绩上传与 Steam 成就上传，
   因此你开作弊玩出的成绩绝不会污染全球排行榜，也不会篡改你真实的 Steam 成就。

游戏内标记状态、经验、解锁动画等照常生效——只有对外上传被拦下。

---

## 背景：原版如何"作弊时禁用解锁"

原版游戏用 `CommandConsole.hasCheated`（开启作弊指令时置 `true`）作为作弊标志，从三处关掉内容：

| 位置 | 原版行为 |
|------|----------|
| `ENV_WarpFissure.CheckCheated` (L140-148) | `hasCheated && blockCheats` 时返回 `true` → `OpenFissure()` 提前返回并弹 "Huh? looks like you cheated or something?"。**只要作弊开启就开不了传送裂隙，永远无法推进到新区域 / 梯子关卡。** |
| `CL_AchievementManager.GameAchievement.GetSteamAchievement` (L401) | `!hasCheated` 才上传 Steam 成就 → 作弊时不上传真实 Steam 成就 |
| `M_Gamemode.Finish` (L202) | `!allowCheatedScores && hasCheated` 时直接 return → 作弊时不上传排行榜成绩 |
| `StatManager.SaveStats` (L85-89) | `hasCheated && !allowCheatedScores` 时直接 return → **作弊时根本不保存进度** |

关键点：**真正卡住"解锁内容"的是传送裂隙闸门**。游戏内解锁其实当场会发生，但若裂隙开不了，
你根本走不到新内容那里；而即便走到了，`SaveStats` 被短路又会让退出后的进度全部丢失。
本模组两处都补上（见下）。

---

## 本模组如何实现

### 1. 作弊时仍可解锁并保存

**a) 传送裂隙（warp fissure）不再拒绝开启（`Patch_ENV_WarpFissure_CheckCheated`）**
> 已用 dnSpy 反编译**实际安装目录**的 `Assembly-CSharp.dll` 验证。

原版 `ENV_WarpFissure.CheckCheated()` 在 `hasCheated && blockCheats` 时返回 `true`，
导致 `OpenFissure()` 提前返回并弹出 "Huh? looks like you cheated or something?"。
**只要作弊开启，你就永远无法打开传送裂隙、无法推进到新区域 / 梯子关卡**——
这才是此前"mod 无效"的真正原因：存档确实被保留了，但你根本开不了裂隙走到新内容那里。

本补丁在 `AllowUnlockWhileCheating` 开启时强制 `CheckCheated()` 返回 `false`（跳过原方法），
裂隙照常打开；`hasCheated` 本身保持 `true`，排行榜闸门、作弊 HUD、Steam 处理仍由 COMLib 接管。

**b) 进度正常保存（`Patch_StatManager_SaveStats`）**
Harmony 前缀在 `StatManager.SaveStats` 调用期间临时把 `CommandConsole.hasCheated` 置 `false`，
让存档正常写入（进度 / 成就 / 经验得以持久化），调用结束后**还原**该标志，
使游戏其余逻辑（排行榜闸门、作弊 HUD 提示）仍能感知真实作弊状态。

以上两项均由配置项 `AllowUnlockWhileCheating`（默认 **true**）控制。

### 2. 调用 `WK_huoyan1231COMLib` 防止污染外部系统（`Plugin.Awake`）
- `LeaderboardManager.DisableForThisRun(guid)` —— 本局不再上传成绩
- `AchievementManager.SetSteamAchievementsDisabled(true)` —— 无条件全局禁用 Steam 成就上传（保留游戏内进度）
- `AchievementManager.DisableForThisRun(guid)` —— 登记本局禁用请求

COMLib 在每局结束（`M_Gamemode.Finish -> ResetAll`）会清空每局禁用请求，
因此本模组在 `M_Gamemode.StartFreshGamemode` 后**再次登记**（见 `Patch_M_Gamemode_StartFreshGamemode`），
保证整场会话的排行榜 / Steam 上传始终被拦截。

通过 `[BepInDependency("huoyan1231.whiteknuckle.comlib", HardDependency)]` 确保 COMLib 先于本模组加载。

---

## 配置（BepInEx 自动生成 `BepInEx/config/huoyan1231.whiteknuckle.unlockwithcheat.cfg`）

| 键 | 默认值 | 说明 |
|----|--------|------|
| `General.AllowUnlockWhileCheating` | `true` | 开启时，作弊模式下的内容解锁（成就/进度/经验）会被正常保存。无论此开关如何，排行榜与 Steam 成就上传都**始终**被本模组禁用。 |

---

## 安装

1. 安装 [BepInEx 5.4.2100](https://github.com/BepInEx/BepInEx/releases)（Mono / net471）。
2. 将以下 DLL 一起放入游戏的 `BepInEx/plugins/` 目录：
   - `WK_UnlockWithCheat.dll`（本模组）
   - `WK_huoyan1231COMLib.dll`（**硬依赖，必须存在**）
3. 启动游戏即可。日志会输出：
   ```
   Plugin huoyan1231.whiteknuckle.unlockwithcheat v1.0.0 loaded.
   Leaderboard & Steam achievement uploads disabled via WK_huoyan1231COMLib.
   ```

> 提示：若缺少 COMLib，BepInEx 会因硬依赖失败而无法加载本模组。

---

## 从源码构建

```bash
# 依赖（Lib/ 下）需已就位：
#   Lib/0Harmony.dll
#   Lib/Assembly-CSharp.dll        (来自游戏本体)
#   Lib/WK_huoyan1231COMLib.dll    (先构建 WK_huoyan1231COMLib)

dotnet build -c Release
# 产物： bin/Release/net471/WK_UnlockWithCheat.dll
```

---

## 依赖

- BepInEx 5.4.2100
- [WK_huoyan1231COMLib](https://github.com/huoyan1231/WK_huoyan1231COMLib) >= 1.1.0（提供 `LeaderboardManager` / `AchievementManager` 两枚共享 API）

---

## 许可

与 White Knuckle 模组生态一致，仅供单机 / 个人使用。请勿将作弊取得的成绩用于正式排行榜竞争。
