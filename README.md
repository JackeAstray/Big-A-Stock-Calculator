# 📈 Big A Stock Calculator (A股做T保本计算器)

[中文介绍](#中文介绍) | [English Introduction](#english-introduction)

---

<h2 id="中文介绍">🇨🇳 中文介绍</h2>

**A股做T保本计算器** 是一款专为中国A股市场股民开发的 Windows 桌面应用程序，用于精准计算“做T”（日内回转交易/T+0）操作的保本价、目标止盈价和止损价。应用内含详尽的费率设置与持仓分析，帮助投资者在复杂的股市波动中科学决策、降低持仓成本。

### 🌟 核心功能

1. **精准的费率设置**  
   - 支持自定义佣金费率（系统默认自带万分之4.3参考）。
   - 支持自定义印花税率（默认万五，单边征收）。
   - 支持“免五”设置（即不足5元是否免收5元最低限制）。
   
2. **涨跌停价格预测**  
   - 输入昨日收盘价，可按主板（10%）或创业板/科创板（20%）自动计算出当天股票的涨停价和跌停价。

3. **双向做T计算 (正T & 倒T)**
   - **正T（先买后卖）**：逢低买入，逢高卖出。
   - **倒T（先卖后买）**：逢高卖出，逢低接回。
   - 支持输入预期盈利（按固定金额或收益率）以及最大承受亏损。一键推算出包含所有手续费的**保本价**、**目标止盈价**和**止损价**。

4. **持仓及降本分析**
   - 填入当前持仓本金和数量，结合现价计算出当前浮动盈亏。
   - 预估本次做T操作成功后，原本的持仓均价能降低到多少（全新持仓成本）。

5. **历史交易记录**
   - 一键保存或清空计算记录，方便复盘和多次交易模拟。

### 🚀 如何使用
1. 下载或克隆本源码仓库。
2. 使用 Visual Studio 2022 或基于 .NET 的现代 IDE 打开解决方案。
3. 编译并运行项目即可使用。

### 🛠 技术栈
- C# & WPF (Windows Presentation Foundation)
- .NET 10
- XAML 界面设计

### 📝 关于与免责声明
- **作者**: [JackeAstray](https://github.com/JackeAstray)
- **项目地址**: [Big-A-Stock-Calculator](https://github.com/JackeAstray/Big-A-Stock-Calculator)
- **免责声明**: 本计算器预测与计算结果仅供学习、参考与复盘使用，**不构成任何投资建议**。股市有风险，投资需谨慎！

---

<h2 id="english-introduction">🇬🇧 English Introduction</h2>

**Big A Stock Calculator** is a Windows Desktop application built specifically for investors in the Chinese A-share market. It helps calculating the exact break-even price, target profit price, and stop-loss price for intraday trading (widely known as "T+0" or "Zuo T" in China) to effectively lower active holding costs.

### 🌟 Key Features

1. **Detailed Fee Configurations**  
   - Customizable commission rates based on your broker.
   - Customizable stamp duty rate (default 0.05% applied to sellers).
   - Option to toggle the "minimum 5 RMB" commission rule.
   
2. **Limit Up & Down Prediction**  
   - Input yesterday's closing price.
   - Select the trading board (Main Board 10% vs. ChiNext/STAR Market 20%) to automatically generate today's upper and lower limit prices.

3. **Bi-directional Intraday Trading (T+0)**
   - **Long T (Buy first, Sell later)**: Buy the dip and sell at a higher price the same day.
   - **Short T (Sell first, Buy later)**: Sell high and buy back lower the same day.
   - Set an expected profit (fixed amount or percentage) and maximum accepted loss. The app instantly provides the precise **Break-Even Price**, **Target Profit Price**, and **Stop-Loss Price** with all trading fees automatically factored in.

4. **Position & Cost Reduction Analysis**
   - Provide your current holding cost and quantity to view immediate floating PNL.
   - Forecasts the **new average holding cost** assuming your planned T+0 trading operation is fully executed successfully.

5. **Historical Records**
   - Log calculation results with a single click and clear history easily for retrospective trading analysis.

### 🚀 How to Use
1. Clone or download this repository.
2. Open the solution in Visual Studio 2022 or a modern .NET IDE.
3. Build and run the project.

### 🛠 Tech Stack
- C# & WPF (Windows Presentation Foundation)
- .NET 10
- XAML UI

### 📝 About & Disclaimer
- **Author**: [JackeAstray](https://github.com/JackeAstray)
- **Repo URL**: [Big-A-Stock-Calculator](https://github.com/JackeAstray/Big-A-Stock-Calculator)
- **Disclaimer**: All predictions and calculations provided by this tool are for educational, reference, and retrospective analysis purposes only, and **do not constitute any investment advice**. The stock market is risky, and investments should be made with caution!
