# TradingAgents（C#）

这是 [TauricResearch/TradingAgents](https://github.com/TauricResearch/TradingAgents) 的 C# 移植，目标框架 `net8.0`，许可证 Apache-2.0。它按固定角色走完一遍研究流程，给出五档评级：`Buy`、`Overweight`、`Hold`、`Underweight`、`Sell`。读不出评级时记为 `REVIEW`，不会悄悄当成 `Hold`。

本程序只写研究记录。它不会向券商发送委托，也不能当作投资建议。

## 决策链

对应上游 LangGraph 的顺序：

1. 选定的分析师写出报告。默认是市场、舆情（`social`）、新闻、基本面。市场报告里的 RSI(14)、SMA(20)、MACD(12,26,9) 在进程内用给定 K 线计算，不再跑多轮工具调用。
2. 多头研究员和空头研究员轮流发言。一轮里每边各说一次，轮数用 `count < 2 * 辩论轮数` 控制。
3. 研究经理给出评级和论点。研究经理与组合经理使用 deep 模型，其余角色使用 quick 模型。
4. 交易员把评级收成 `Buy` / `Hold` / `Sell`。`Overweight` 对应买入，`Underweight` 对应卖出，进场和止损写绝对价格。
5. 激进、保守、中性三个风险席按这个顺序发言，轮数用 `count < 3 * 风险轮数` 控制。
6. 组合经理写出最终决策。五档评级从正文解析。
7. 记忆日志追加一条待结算记录。已有同一日期和代码的记录不会再写一遍。

辩论保持英文。分析师、研究经理、交易员和组合经理的报告按 `--language` 指定的语言撰写。

## 行情与模型

当前数据源是 `OfflineMarketData`：以决策日为终点生成 60 根收盘价 `100 + i * 0.4` 的样本，并标明 `offline sample, not a live market`。新闻、基本面和舆情同样是样本句子。这些数字不是实盘报价，也没有按公告日做点时对齐，因为样本里没有真实公告。

`--offline` 使用脚本模型，不访问网络，也不需要密钥。测试走这条路径。

不带 `--offline` 时，客户端调用 [xAI Responses API](https://docs.x.ai/developers/quickstart)：

- 地址默认 `https://api.x.ai/v1/responses`
- 模型默认 `grok-4.7`（quick 与 deep 相同，可用环境变量分开）
- 密钥按 `TRADINGAGENTS_LLM_API_KEY`、`XAI_API_KEY`、`OPENAI_API_KEY` 的顺序读取
- 请求超时默认 180 秒，可用 `TRADINGAGENTS_LLM_TIMEOUT_SECONDS` 加长
- 日志和错误文本里不保留密钥

`TRADINGAGENTS_LLM_BACKEND_URL` 可以换成别的 OpenAI 兼容主机。`TRADINGAGENTS_LLM_API=chat` 时改为 `POST /chat/completions`。模型名要用那台主机实际提供的名字，例如 `TRADINGAGENTS_QUICK_THINK_LLM` 和 `TRADINGAGENTS_DEEP_THINK_LLM`。

## 持仓、记忆和检查点

持仓有三种状态，提示词里会分开处理：

- 传入持仓：列出该标的的数量和均价
- 传入空账本：写明没有该标的仓位
- 不传账本：提示词里不出现持仓段

`--checkpoint` 把 `AgentState` 存成 JSON。线程号是代码、日期、分析师、轮数和持仓指纹的 SHA-256 前 16 位。已完成的阶段不会再次调用模型。账本变了就不会续上一次运行。

记忆文件默认是 `~/.tradingagents/memory/trading_memory.md`，也可以用 `--memory` 或 `TRADINGAGENTS_MEMORY_LOG_PATH`。待结算条目不会进入后续提示，也不会被条数上限删掉。结算由调用方执行 `TradingMemoryLog.Settle`，写入收益率、超额收益、持有天数和 `REFLECTION`。本移植不会自己去拉价格来结算。`as_of` 只保留 `resolved:` 日期不晚于决策日的条目。

## 构建和运行

本机若没有全局 `dotnet` SDK，构建脚本会使用 `C:\Users\ADMINI~1\AppData\Local\Temp\2\dotnet\dotnet.exe`。

```powershell
.\build.ps1
```

离线跑一遍：

```powershell
.\src\TradingAgents.Cli\bin\Release\net8.0\tradingagents.exe --ticker NVDA --date 2026-01-15 --offline --output .\results
```

带密钥时去掉 `--offline`。退出码：`0` 成功或帮助，`1` 运行失败，`64` 参数错误。

## 相对上游没有带上的部分

上游还有 Yahoo、SEC、Reddit、FRED、Polymarket 等数据供应商，多供应商 LLM 目录，回测，HTML 报告，以及按真实价格自动结算。这里都没有实现。指标只用本地样本 K 线，一次计算后交给模型写评论。

论文见 [arXiv:2412.20138](https://arxiv.org/abs/2412.20138)。
