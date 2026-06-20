# OverTheHillMod

## 中文

这是一个用于 `over the hill Demo` 的 BepInEx IL2CPP 模组，提供车辆、天气、时间和少量玩家资源调整。

### 面板

- `F10`：车辆控制面板
- `F9`：天气与时间面板
- 面板内可切换 `中文 / English`，两个面板共用同一个语言设置

### F10 车辆功能

- 车辆调校总开关
- 车辆预设：原版、稳定越野、公路轻快、牵引爬坡
- 动力与传动：扭矩、功率上限、齿比、抓地、转速上限
- 稳定与保护：重心 Y 偏移、下压力、低重力、速度和角速度限制
- 原生底盘：转向角、悬挂行程、弹簧刚度、阻尼、滚动阻力、地面穿透容差
- 原生操控：刹车倍率、线性阻力、角阻尼、受伤倍率、转向辅助、ABS/TCS/ESC 覆盖
- 常用操作：修复车辆、补充已装备装置、货币清零、添加 10 货币

### F9 天气与时间功能

- 切换游戏原生天气预设
- 直接调整天气参数：降水、雪、地表湿度、云、雾、光照、风
- 调整并锁定游戏时间
- 记录天气参数和原生天气 ID 到 BepInEx 日志
- 可选场景材质扫描，用于写入雨雪和地表相关参数

### 安全范围

车辆参数范围经过限制，避免过大的物理数值导致卡地、翻车、同步异常或数值溢出。动力相关参数比阻尼、刹车、抓地等参数更保守。

### 注意

- 需要进入地图上车后，车辆和底盘参数才会完整显示。
- 天气功能需要进入地图后刷新天气系统。
- 本模组不修改车辆和部件解锁。

## English

This is a BepInEx IL2CPP mod for `over the hill Demo`. It provides vehicle, weather, time, and limited player resource controls.

### Panels

- `F10`: Vehicle Control Panel
- `F9`: Weather and Time Panel
- The panels can switch between `中文 / English`; both panels share the same language setting

### F10 Vehicle Features

- Main vehicle tuning toggle
- Vehicle presets: Vanilla, Trail, Road, Climb
- Powertrain: torque, power limit, gear ratio, grip, RPM limit
- Stability and protection: center of mass Y offset, downforce, low gravity, speed and angular velocity limits
- Native chassis: steering angle, suspension travel, spring rate, damper rate, rolling resistance, ground penetration tolerance
- Native handling: brake multiplier, linear drag, angular drag, damage multiplier, steering assist, ABS/TCS/ESC override
- Actions: repair vehicle, refill equipped tools, clear money, add 10 money

### F9 Weather and Time Features

- Switch native game weather presets
- Direct weather controls: precipitation, snow, ground wetness, clouds, fog, light, wind
- Adjust and lock game time
- Log weather parameters and native weather IDs to the BepInEx log
- Optional scene material scan for rain, snow, and ground-related parameters

### Safety Ranges

Vehicle values are clamped to avoid extreme physics values that may cause stuck vehicles, rollovers, sync errors, or numeric overflow. Powertrain values are kept more conservative than drag, braking, and grip values.

### Notes

- Enter a map and get into a vehicle before vehicle and chassis parameters are fully available.
- Enter the map and refresh the weather system before using weather controls.
- This mod does not modify vehicle or part unlocks.
