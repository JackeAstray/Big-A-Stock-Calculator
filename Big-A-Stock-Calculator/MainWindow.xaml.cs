using System;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Text.Json;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Shapes;

namespace Big_A_Stock_Calculator
{
    public class UserSettings
    {
        public string CommissionRate { get; set; } = "0.00043";
        public string StampDutyRate { get; set; } = "0.0005";
        public bool IsFree5 { get; set; } = true;
        public string CostPrice { get; set; } = "";
        public string HoldingQuantity { get; set; } = "";
    }

    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : Window
    {
        // 固定的过户费率（双向收取），十万分之1
        private const decimal TransferFeeRate = 0.00001m;
        private readonly string SettingFilePath = System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "settings.json");
        private Process _llamaProcess = null;

        public MainWindow()
        {
            InitializeComponent();
            LoadModels();
            LoadSettings();
            this.Closing += MainWindow_Closing;
        }

        private void LoadModels()
        {
            string baseDir = AppDomain.CurrentDomain.BaseDirectory;
            string modelsDir = System.IO.Path.Combine(baseDir, "Models");
            string appSettingsPath = System.IO.Path.Combine(baseDir, "appsettings.json");
            
            string defaultModel = "gemma-4-E4B-it-Q4_K_M.gguf";

            if (File.Exists(appSettingsPath))
            {
                try
                {
                    var json = File.ReadAllText(appSettingsPath);
                    var doc = JsonDocument.Parse(json);
                    if (doc.RootElement.TryGetProperty("ModelName", out var modelElement))
                    {
                        defaultModel = modelElement.GetString() ?? defaultModel;
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"加载模型配置出错: {ex}");
                }
            }

            if (Directory.Exists(modelsDir))
            {
                var files = Directory.GetFiles(modelsDir, "*.gguf");
                foreach (var file in files)
                {
                    string fileName = System.IO.Path.GetFileName(file);
                    ModelComboBox.Items.Add(fileName);
                }
            }

            if (ModelComboBox.Items.Count > 0)
            {
                if (ModelComboBox.Items.Contains(defaultModel))
                {
                    ModelComboBox.SelectedItem = defaultModel;
                }
                else
                {
                    ModelComboBox.SelectedIndex = 0;
                }
            }
            else
            {
                ModelComboBox.Items.Add("无可用模型 (*.gguf)");
                ModelComboBox.SelectedIndex = 0;
                ModelComboBox.IsEnabled = false;
            }
        }

        private void LoadSettings()
        {
            try
            {
                if (File.Exists(SettingFilePath))
                {
                    string json = File.ReadAllText(SettingFilePath);
                    var settings = JsonSerializer.Deserialize<UserSettings>(json);
                    if (settings != null)
                    {
                        CommissionRateTextBox.Text = settings.CommissionRate;
                        StampDutyRateTextBox.Text = settings.StampDutyRate;
                        IsFree5CheckBox.IsChecked = settings.IsFree5;
                        CostPriceTextBox.Text = settings.CostPrice;
                        HoldingQuantityTextBox.Text = settings.HoldingQuantity;
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"加载设置出错: {ex}");
            }
        }

        private void SaveSettings()
        {
            try
            {
                var settings = new UserSettings
                {
                    CommissionRate = CommissionRateTextBox.Text,
                    StampDutyRate = StampDutyRateTextBox.Text,
                    IsFree5 = IsFree5CheckBox.IsChecked ?? true,
                    CostPrice = CostPriceTextBox.Text,
                    HoldingQuantity = HoldingQuantityTextBox.Text
                };
                string json = JsonSerializer.Serialize(settings, new JsonSerializerOptions { WriteIndented = true });
                File.WriteAllText(SettingFilePath, json);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"保存设置出错: {ex}");
            }
        }

        private void MainWindow_Closing(object sender, System.ComponentModel.CancelEventArgs e)
        {
            SaveSettings();
            try 
            {
                if (_llamaProcess != null && !_llamaProcess.HasExited)
                {
                    _llamaProcess.Kill(true);
                    _llamaProcess.Dispose();
                }
            } 
            catch (Exception ex)
            {
                Console.WriteLine($"关闭进程出错: {ex}");
            }
        }

        private void CalculateButton_Click(object sender, RoutedEventArgs e)
        {
            // 清空错误提示
            ErrorTextBlock.Visibility = Visibility.Collapsed;
            ErrorTextBlock.Text = string.Empty;

            try
            {
                // 1. 获取并验证输入数据
                if (!decimal.TryParse(CommissionRateTextBox.Text, out decimal commissionRate))
                    throw new Exception("佣金费率格式不正确");
                if (!decimal.TryParse(StampDutyRateTextBox.Text, out decimal stampDutyRate))
                    throw new Exception("印花税率格式不正确");
                if (!decimal.TryParse(BuyPriceTextBox.Text, out decimal buyPrice))
                    throw new Exception("买入交易单价格式不正确");
                if (!int.TryParse(BuyQuantityTextBox.Text, out int buyQuantity) || buyQuantity < 0 || (buyQuantity != 0 && buyQuantity % 100 != 0))
                    throw new Exception("买入交易股数必须为0或者100的整数倍");
                if (!decimal.TryParse(SellPriceTextBox.Text, out decimal sellPrice))
                    throw new Exception("卖出交易单价格式不正确");
                if (!int.TryParse(SellQuantityTextBox.Text, out int sellQuantity) || sellQuantity < 0 || (sellQuantity != 0 && sellQuantity % 100 != 0))
                    throw new Exception("卖出交易股数必须为0或者100的整数倍");

                bool isBuyValid = buyPrice > 0 && buyQuantity > 0;
                bool isSellValid = sellPrice > 0 && sellQuantity > 0;

                if (!isBuyValid && !isSellValid)
                {
                    throw new Exception("必须至少输入有效的买入或卖出数据(价格和股数都必须大于0)");
                }


                bool isPositiveT = true; // Hardcoded default based on UI change
                bool isFree5 = IsFree5CheckBox.IsChecked == true;

                // Profit calculation disabled as TargetProfitTextBox is commented out
                decimal targetProfit = 0; // Temporary placeholder
                // if (ProfitModeComboBox.SelectedIndex == 1) // 按收益率
                // {
                //     targetProfit = (price * quantity) * (targetProfitInput / 100m);
                // }

                // 卖出逻辑防呆校验：校验是不是数量超卖
                if (isSellValid)
                {
                    if (int.TryParse(AvailableQuantityTextBox.Text, out int availableQty) && sellQuantity > availableQty)
                    {
                        throw new Exception("交易卖出股数不能大于当前的【可卖数量】！");
                    }
                    else if (int.TryParse(HoldingQuantityTextBox.Text, out int holdingQty) && sellQuantity > holdingQty)
                    {
                        throw new Exception("交易卖出股数不能大于当前的【持有股份总数】！");
                    }
                }

                // 2. 调用核心计算逻辑
                CalculateBreakeven(isBuyValid ? buyPrice : 0m, isBuyValid ? buyQuantity : 0, 
                                   isSellValid ? sellPrice : 0m, isSellValid ? sellQuantity : 0, 
                                   commissionRate, stampDutyRate, isPositiveT, isFree5, targetProfit, 0 /*maxLoss*/);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"计算过程中出错: {ex.ToString()}"); // ADDED: Print complete exception details
                ErrorTextBlock.Text = $"输入错误：{ex.Message}";
                ErrorTextBlock.Visibility = Visibility.Visible;
                TotalFeeTextBlock.Text = "--- 元";
                if (FeeDetailTextBlock != null) FeeDetailTextBlock.Text = "[ 明细: 佣金 --- | 印花税 --- | 过户费 --- ]";
                TargetPriceTextBlock.Text = "--- 元";
                if (ProfitTargetPriceTextBlock != null) ProfitTargetPriceTextBlock.Text = "--- 元";
                if (StopLossPriceTextBlock != null) StopLossPriceTextBlock.Text = "--- 元";
                if (HoldingPnLTextBlock != null) HoldingPnLTextBlock.Text = "---";
                if (NewCostPriceTextBlock != null) NewCostPriceTextBlock.Text = "---";
            }
        }

        /// <summary>
        /// 核心计算逻辑：计算包含全场景单边和双边的交易明细
        /// </summary>
        private void CalculateBreakeven(decimal buyPrice, int buyQty, decimal sellPrice, int sellQty, decimal commissionRate, decimal stampDutyRate, bool isPositiveT, bool isFree5, decimal targetProfit, decimal maxLoss)
        {
            decimal buyTurnover = buyPrice * buyQty;
            decimal sellTurnover = sellPrice * sellQty;

            // 买入费用计算
            decimal buyCommission = 0m;
            decimal buyTransferFee = 0m;
            decimal buyFee = 0m;
            if (buyQty > 0)
            {
                buyCommission = buyTurnover * commissionRate;
                if (!isFree5 && buyCommission < 5m) buyCommission = 5m;
                buyTransferFee = buyTurnover * TransferFeeRate;
                buyFee = buyCommission + buyTransferFee;
            }

            // 卖出费用计算
            decimal sellCommission = 0m;
            decimal sellTransferFee = 0m;
            decimal sellStampDuty = 0m;
            decimal sellFee = 0m;
            if (sellQty > 0)
            {
                sellCommission = sellTurnover * commissionRate;
                if (!isFree5 && sellCommission < 5m) sellCommission = 5m;
                sellTransferFee = sellTurnover * TransferFeeRate;
                sellStampDuty = sellTurnover * stampDutyRate;
                sellFee = sellCommission + sellTransferFee + sellStampDuty;
            }

            decimal totalCommission = buyCommission + sellCommission;
            decimal totalTransferFee = buyTransferFee + sellTransferFee;
            decimal totalStampDuty = sellStampDuty;

            // 总手续费
            decimal totalFee = buyFee + sellFee;

            // 保本与目标价计算（只有当两边数字都完整存在且等量时差价才有保本意义。如果是不等量/单边，就仅计算手续费）
            decimal targetPrice = 0m;
            decimal profitTargetPrice = 0m;
            decimal stopLossPrice = 0m;

            int refQty = Math.Max(buyQty, sellQty); // fallback to max
            if (buyQty > 0 && sellQty > 0 && buyQty == sellQty)
            {
                refQty = buyQty;
                decimal p = buyPrice; // Fallback anchor based on Positive/Negative T if needed? For now just use BuyPrice as starting base for "buy first"
                if (!isPositiveT) p = sellPrice;

                decimal breakevenDiff = totalFee / refQty;
                decimal profitDiff = targetProfit / refQty;
                decimal lossDiff = maxLoss / refQty;

                if (isPositiveT)
                {
                    targetPrice = buyPrice + breakevenDiff;
                    profitTargetPrice = buyPrice + breakevenDiff + profitDiff;
                    stopLossPrice = buyPrice + breakevenDiff - lossDiff;
                }
                else
                {
                    targetPrice = sellPrice - breakevenDiff;
                    profitTargetPrice = sellPrice - breakevenDiff - profitDiff;
                    stopLossPrice = sellPrice - breakevenDiff + lossDiff;
                }
            }

            // 更新UI结果显示
            TotalFeeTextBlock.Text = $"{Math.Round(totalFee, 2)} 元";
            if (FeeDetailTextBlock != null)
                FeeDetailTextBlock.Text = $"[ 明细: 佣金 {Math.Round(totalCommission, 2)} | 印花税 {Math.Round(totalStampDuty, 2)} | 过户费 {Math.Round(totalTransferFee, 2)} ]";

            // 计算实际盈亏预估（即使不等量或者单边，也可以根据实际营收差额 - 总费用计算）
            decimal netProfit = 0m;

            // ====== 单边买入：预估保本卖出价 ======
            if (buyQty > 0 && sellQty == 0)
            {
                // 买入单边时，提示需要卖什么价格才能不亏手续费
                decimal breakevenSellPrice = buyPrice + (buyFee / buyQty);
                TargetPriceTextBlock.Text = $"预估保本卖价：{Math.Round(breakevenSellPrice, 3)} 元";
                ProfitTargetPriceTextBlock.Text = "--- 元";
                if (StopLossPriceTextBlock != null) StopLossPriceTextBlock.Text = "--- 元";
            }
            // ====== 单边卖出：预估保本买入价 ======
            else if (sellQty > 0 && buyQty == 0)
            {
                // 卖出单边时，提示要用什么价格接回才不亏手续费
                decimal breakevenBuyPrice = sellPrice - (sellFee / sellQty);
                TargetPriceTextBlock.Text = $"预估接回价：{Math.Round(breakevenBuyPrice, 3)} 元";
                ProfitTargetPriceTextBlock.Text = "--- 元";
                if (StopLossPriceTextBlock != null) StopLossPriceTextBlock.Text = "--- 元";
            }
            // ====== 双边都有 T 操作 ======
            else if (buyQty > 0 && sellQty > 0)
            {
                // 买入和卖出都发生的时候，实际盈亏 = 卖出的钱 - 买入的钱 - 本次双边产生的所有的手续费。 
                netProfit = (sellTurnover) - (buyTurnover) - totalFee;

                if (buyQty == sellQty)
                {
                    TargetPriceTextBlock.Text = $"{Math.Round(netProfit, 2)} 元";
                    ProfitTargetPriceTextBlock.Text = $"{Math.Round(profitTargetPrice, 3)} 元";
                    if (StopLossPriceTextBlock != null) StopLossPriceTextBlock.Text = $"{Math.Round(stopLossPrice, 3)} 元";
                }
                else
                {
                    TargetPriceTextBlock.Text = $"波段净现金流：{Math.Round(netProfit, 2)} 元";
                    ProfitTargetPriceTextBlock.Text = "--- 元";
                    if (StopLossPriceTextBlock != null) StopLossPriceTextBlock.Text = "--- 元";
                }
            }
            else
            {
                TargetPriceTextBlock.Text = "--- 元";
                ProfitTargetPriceTextBlock.Text = "--- 元";
                if (StopLossPriceTextBlock != null) StopLossPriceTextBlock.Text = "--- 元";
            }

            // 进行持仓扩展分析 (传参这里我们把本次操作挣下来的净利传入用作降本)
            CalculateHoldingPnL(buyQty > 0 ? buyPrice : sellPrice, refQty, buyQty > 0 && sellQty > 0 ? netProfit : 0m);
        }

        // ================ 各个额外快捷按钮事件 ================
        
        private void ResetButton_Click(object sender, RoutedEventArgs e)
        {
            ErrorTextBlock.Visibility = Visibility.Collapsed;
            BuyPriceTextBox.Text = "20.20";
            BuyQuantityTextBox.Text = "200";
            SellPriceTextBox.Text = "21.20";
            SellQuantityTextBox.Text = "200";
            YesterdayCloseTextBox.Text = "";
            LimitUpTextBlock.Text = "---";
            LimitDownTextBlock.Text = "---";
            
            CostPriceTextBox.Text = "";
            HoldingQuantityTextBox.Text = "";
            CurrentPriceTextBox.Text = "";
            
            if (AddPositionPriceTextBox != null) AddPositionPriceTextBox.Text = "";
            if (AddPositionQuantityTextBox != null) AddPositionQuantityTextBox.Text = "";
            if (AddPositionNewCostTextBlock != null) AddPositionNewCostTextBlock.Text = "---";
            if (AddPositionDiffTextBlock != null) AddPositionDiffTextBlock.Text = "";
            
            TotalFeeTextBlock.Text = "--- 元";
            if (FeeDetailTextBlock != null) FeeDetailTextBlock.Text = "[ 明细: 佣金 --- | 印花税 --- | 过户费 --- ]";
            TargetPriceTextBlock.Text = "--- 元";
            ProfitTargetPriceTextBlock.Text = "--- 元";
            if (StopLossPriceTextBlock != null) StopLossPriceTextBlock.Text = "--- 元";
            
            HoldingPnLTextBlock.Text = "---";
            NewCostPriceTextBlock.Text = "---";
        }

        private void YesterdayClose_Update(object sender, RoutedEventArgs e)
        {
            if (LimitUpTextBlock == null || LimitDownTextBlock == null) return;
            if (decimal.TryParse(YesterdayCloseTextBox?.Text, out decimal closePrice) && closePrice > 0)
            {
                decimal factor = BoardTypeComboBox.SelectedIndex == 0 ? 0.1m : 0.2m;
                // A股标准四舍五入
                decimal limitUp = Math.Round(closePrice * (1 + factor), 2, MidpointRounding.AwayFromZero);
                decimal limitDown = Math.Round(closePrice * (1 - factor), 2, MidpointRounding.AwayFromZero);
                LimitUpTextBlock.Text = limitUp.ToString("0.00");
                LimitDownTextBlock.Text = limitDown.ToString("0.00");
            }
            else
            {
                LimitUpTextBlock.Text = "---";
                LimitDownTextBlock.Text = "---";
            }
        }

        private void SaveRecordButton_Click(object sender, RoutedEventArgs e)
        {
            if (TargetPriceTextBlock.Text.Contains("---")) return;

            string mode = "📈 正T(先买后卖)";
            string price = BuyPriceTextBox.Text;
            string qty = BuyQuantityTextBox.Text;
            string target = ProfitTargetPriceTextBlock.Text;
            string loss = StopLossPriceTextBlock != null ? StopLossPriceTextBlock.Text : "---";
            string breakeven = TargetPriceTextBlock.Text;
            string fee = TotalFeeTextBlock.Text;

            string expectedProfit = "---";
            // if (ProfitModeComboBox.SelectedIndex == 1) expectedProfit += "%"; else expectedProfit += "元";

            StringBuilder sb = new StringBuilder();
            sb.AppendLine($"============== 策略卡片 ==============");
            sb.AppendLine($"[时间] {DateTime.Now:HH:mm:ss}");
            sb.AppendLine($"[方向] {mode}");
            sb.AppendLine($"[基准] 单价: {price} 元 | 数量: {qty} 股");
            sb.AppendLine($"[预期] 目标利润: {expectedProfit} (预估手续费 {fee})");
            sb.AppendLine($"--------------------------------------");
            sb.AppendLine($"🎯 目标止盈挂单价: {target}");
            sb.AppendLine($"🛡️ 保本撤退基准价: {breakeven}");
            sb.AppendLine($"⚠️ 极限止损离场价: {loss}");

            if (!NewCostPriceTextBlock.Text.Contains("---"))
            {
                sb.AppendLine($"✨ 策略成功后，新持仓均价将被降至: {NewCostPriceTextBlock.Text}");
            }
            sb.AppendLine($"======================================");

            HistoryListBox.Items.Insert(0, sb.ToString());
        }

        private void ClearHistoryButton_Click(object sender, RoutedEventArgs e)
        {
            HistoryListBox.Items.Clear();
        }

        private async void AIAssistantButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                string mode = "正T(先买后卖)";
                string price = BuyPriceTextBox.Text;
                string qty = BuyQuantityTextBox.Text;
                string currentCost = CostPriceTextBox.Text;
                string holdingQty = HoldingQuantityTextBox.Text;
                string target = ProfitTargetPriceTextBlock.Text;
                
                string prompt = $@"
你是一个专业的A股交易助手。用户目前正在进行股票操作。
【当前持仓信息】
当前的持有股数：{holdingQty}
当前的成本价：{currentCost}元。
【交易需求】
打算进行操作的方向：{mode}
计划的交易价格：{price}元
交易的数量：{qty}股。
期待的止盈目标：{target}。

请你根据目前的信息，以简短的语言给出一些交易策略或建议指导。
";

                string baseDir = AppDomain.CurrentDomain.BaseDirectory;
                string llamaServerPath = System.IO.Path.Combine(baseDir, "Exe", "llama-server.exe");
                
                string selectedModelName = ModelComboBox.SelectedItem?.ToString();
                if (string.IsNullOrEmpty(selectedModelName) || selectedModelName.Contains("无可用模型"))
                {
                    MessageBox.Show("请先在下拉框选择有效的模型文件！", "模型未选择", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }
                
                string modelPath = System.IO.Path.Combine(baseDir, "Models", selectedModelName);
                string appSettingsPath = System.IO.Path.Combine(baseDir, "appsettings.json");

                int port = 8080;
                if (File.Exists(appSettingsPath))
                {
                    try
                    {
                        var json = File.ReadAllText(appSettingsPath);
                        var doc = JsonDocument.Parse(json);
                        if (doc.RootElement.TryGetProperty("LlamaServerPort", out var portElement))
                        {
                            port = portElement.GetInt32();
                        }
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"读取LlamaServerPort配置出错: {ex}");
                    }
                }

                if (!File.Exists(llamaServerPath) || !File.Exists(modelPath))
                {
                    MessageBox.Show("未找到模型文件或 llama-server.exe，请查阅 README 添加模型及组件！", "AI 组件缺失", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }

                var button = sender as Button;
                if (button != null) button.IsEnabled = false;

                // 检查是否已经启动了后台进程
                if (_llamaProcess == null || _llamaProcess.HasExited)
                {
                    // 额外检查系统的端口上有无同名进程在跑，如没有就自己跑
                    var processes = Process.GetProcessesByName("llama-server");
                    if (processes.Length == 0)
                    {
                        HistoryListBox.Items.Insert(0, "[启动 AI 服务器引擎中... 模型加载需要十多秒，请稍候]");
                        var startInfo = new ProcessStartInfo
                        {
                            FileName = llamaServerPath,
                            Arguments = $"-m \"{modelPath}\" --port {port} -c 2048",
                            UseShellExecute = false,
                            CreateNoWindow = true,
                            WorkingDirectory = System.IO.Path.Combine(baseDir, "Exe")
                        };
                        string dllPath = System.IO.Path.Combine(baseDir, "Dll");
                        if (startInfo.Environment.ContainsKey("PATH"))
                        {
                            startInfo.Environment["PATH"] = dllPath + ";" + startInfo.Environment["PATH"];
                        }
                        else
                        {
                            startInfo.Environment["PATH"] = dllPath;
                        }
                        
                        // We capture standard output and error to check for startup issues
                        startInfo.RedirectStandardOutput = true;
                        startInfo.RedirectStandardError = true;

                        _llamaProcess = Process.Start(startInfo);
                        
                        // 预留 15-20 秒让模型加载进内存
                        int waitLoops = 20;
                        while (waitLoops > 0 && !_llamaProcess.HasExited)
                        {
                            await System.Threading.Tasks.Task.Delay(1000);
                            waitLoops--;
                        }
                        
                        if (_llamaProcess.HasExited)
                        {
                            string error = await _llamaProcess.StandardError.ReadToEndAsync();
                            MessageBox.Show($"AI 引擎启动失败！\n错误信息：{error}", "引擎启动错误", MessageBoxButton.OK, MessageBoxImage.Error);
                            HistoryListBox.Items.RemoveAt(0);
                            if (button != null) button.IsEnabled = true;
                            return;
                        }

                        HistoryListBox.Items.RemoveAt(0);
                    }
                }

                // Append status indicating it's thinking
                HistoryListBox.Items.Insert(0, "[AI 思考中，请稍候...]");

                string result = "AI 请求失败。";
                try
                {
                    using var client = new HttpClient();
                    client.Timeout = TimeSpan.FromMinutes(2);
                    string apiUrl = $"http://127.0.0.1:{port}/completion";
                    var reqBody = new
                    {
                        prompt = prompt,
                        n_predict = 256
                    };
                    string jsonContent = JsonSerializer.Serialize(reqBody);
                    var content = new StringContent(jsonContent, Encoding.UTF8, "application/json");

                    var response = await client.PostAsync(apiUrl, content);
                    response.EnsureSuccessStatusCode();

                    var respString = await response.Content.ReadAsStringAsync();
                    using var jsonDoc = JsonDocument.Parse(respString);
                    result = jsonDoc.RootElement.GetProperty("content").GetString();
                }
                catch (Exception ex)
                {
                    result = "调用本地服务器失败: " + ex.Message;
                }

                if (button != null) button.IsEnabled = true;
                if (HistoryListBox.Items.Count > 0 && HistoryListBox.Items[0].ToString().Contains("[AI 思考中"))
                {
                    HistoryListBox.Items.RemoveAt(0);
                }
                
                // Add AI response to history
                StringBuilder sb = new StringBuilder();
                sb.AppendLine($"============== AI 建议 ==============");
                sb.AppendLine($"[时间] {DateTime.Now:HH:mm:ss}");
                sb.AppendLine(result?.Trim() ?? "");
                sb.AppendLine($"======================================");
                HistoryListBox.Items.Insert(0, sb.ToString());
            }
            catch (Exception ex)
            {
                MessageBox.Show($"AI调用出现异常: {ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
                var btn = sender as Button;
                if (btn != null) btn.IsEnabled = true;
                if (HistoryListBox.Items.Count > 0 && HistoryListBox.Items[0].ToString().Contains("[AI 思考中"))
                {
                    HistoryListBox.Items.RemoveAt(0);
                }
            }
        }

        private void AddPosition_Update(object sender, TextChangedEventArgs e)
        {
            if (AddPositionNewCostTextBlock == null) return;

            // 1. Calculate PnL Live
            if (HoldingPnLTextBlock != null)
            {
                if (decimal.TryParse(CostPriceTextBox?.Text, out decimal cp) && cp > 0 &&
                    int.TryParse(HoldingQuantityTextBox?.Text, out int hq) && hq > 0 &&
                    decimal.TryParse(string.IsNullOrWhiteSpace(CurrentPriceTextBox?.Text) ? BuyPriceTextBox?.Text : CurrentPriceTextBox?.Text, out decimal rp) && rp > 0)
                {
                    decimal currentPnL = (rp - cp) * hq;
                    HoldingPnLTextBlock.Text = $"{Math.Round(currentPnL, 2)} 元";
                    HoldingPnLTextBlock.Foreground = currentPnL > 0 ? (Brush)FindResource("TechRed") : (currentPnL < 0 ? (Brush)FindResource("TechGreen") : Brushes.White);
                }
                else
                {
                    HoldingPnLTextBlock.Text = "---";
                    HoldingPnLTextBlock.Foreground = Brushes.White;
                }
            }

            // 2. Add position simulation
            if (decimal.TryParse(CostPriceTextBox?.Text, out decimal currentCost) && currentCost > 0 &&
                int.TryParse(HoldingQuantityTextBox?.Text, out int currentQty) && currentQty > 0 &&
                decimal.TryParse(AddPositionPriceTextBox?.Text, out decimal addPrice) && addPrice > 0 &&
                int.TryParse(AddPositionQuantityTextBox?.Text, out int addQty) && addQty > 0)
            {
                decimal commissionRate = decimal.TryParse(CommissionRateTextBox?.Text, out decimal cr) ? cr : 0.00025m;
                bool isFree5 = IsFree5CheckBox?.IsChecked == true;

                decimal turnover = addPrice * addQty;
                decimal commission = turnover * commissionRate;
                if (!isFree5 && commission < 5m) commission = 5m;

                decimal transferFee = turnover * TransferFeeRate;

                decimal totalCost = (currentCost * currentQty) + turnover + commission + transferFee;
                int totalQty = currentQty + addQty;

                decimal newAverageCost = totalCost / totalQty;
                decimal diff = newAverageCost - currentCost;

                AddPositionNewCostTextBlock.Text = $"{Math.Round(newAverageCost, 3)} 元";

                if (AddPositionDiffTextBlock != null)
                {
                    if (diff > 0)
                    {
                        AddPositionDiffTextBlock.Text = $" (变高 {Math.Round(diff, 3)} 元)";
                        AddPositionDiffTextBlock.Foreground = (Brush)FindResource("TechRed");
                    }
                    else if (diff < 0)
                    {
                        AddPositionDiffTextBlock.Text = $" (变低 {Math.Round(-diff, 3)} 元)";
                        AddPositionDiffTextBlock.Foreground = (Brush)FindResource("TechGreen");
                    }
                    else
                    {
                        AddPositionDiffTextBlock.Text = $" (不变)";
                        AddPositionDiffTextBlock.Foreground = Brushes.White;
                    }
                }
            }
            else
            {
                AddPositionNewCostTextBlock.Text = "---";
                if (AddPositionDiffTextBlock != null) AddPositionDiffTextBlock.Text = "";
            }
        }

        private void FillQuantity(double fraction, bool isBuy)
        {
            if (int.TryParse(HoldingQuantityTextBox.Text, out int holdingQty) && holdingQty > 0)
            {
                int targetQty = (int)(holdingQty * fraction);
                targetQty = (targetQty / 100) * 100; // 向下取整到100的倍数
                if (targetQty > 0)
                {
                    if (isBuy) BuyQuantityTextBox.Text = targetQty.ToString();
                    else SellQuantityTextBox.Text = targetQty.ToString();
                }
                else
                {
                    MessageBox.Show("按该比例换算后不足100股无法交易。", "提示", MessageBoxButton.OK, MessageBoxImage.Information);
                }
            }
            else
            {
                MessageBox.Show("请先在下方【持仓及降本分析】区域内填写当前的【持有股份总数】！", "仓位计算提示", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }

        private void BtnFullPosition_Click(object sender, RoutedEventArgs e) => FillQuantity(1.0, true);
        private void BtnHalfPosition_Click(object sender, RoutedEventArgs e) => FillQuantity(0.5, true);
        private void BtnOneThirdPosition_Click(object sender, RoutedEventArgs e) => FillQuantity(1.0 / 3.0, true);

        private void BtnSellFullPosition_Click(object sender, RoutedEventArgs e) => FillQuantity(1.0, false);
        private void BtnSellHalfPosition_Click(object sender, RoutedEventArgs e) => FillQuantity(0.5, false);
        private void BtnSellOneThirdPosition_Click(object sender, RoutedEventArgs e) => FillQuantity(1.0 / 3.0, false);


        /// <summary>
        /// 分析做T完成后的持仓和成本变化
        /// </summary>
        private void CalculateHoldingPnL(decimal p, int n, decimal expectedProfit)
        {
            if (decimal.TryParse(CostPriceTextBox.Text, out decimal costPrice) && costPrice > 0 &&
                int.TryParse(HoldingQuantityTextBox.Text, out int holdingQty) && holdingQty > 0)
            {
                // 以用户当前输入的“当前现价”或者首笔交易单价估算当前的浮动盈亏状态
                decimal referencePrice = p;
                if (!string.IsNullOrWhiteSpace(CurrentPriceTextBox?.Text) && 
                    decimal.TryParse(CurrentPriceTextBox.Text, out decimal currentPrice) && currentPrice > 0)
                {
                    referencePrice = currentPrice;
                }

                decimal currentPnL = (referencePrice - costPrice) * holdingQty;
                HoldingPnLTextBlock.Text = $"{Math.Round(currentPnL, 2)} 元";
                HoldingPnLTextBlock.Foreground = currentPnL > 0 ? (Brush)FindResource("TechRed") : (currentPnL < 0 ? (Brush)FindResource("TechGreen") : Brushes.White);

                // 核心指标：一旦完成这波 T 以后，新成本被摊薄为多少
                // 原总成本 - 赚到的净利润现金 = 新的总成本。 除以总股数即为新成本价
                decimal originalTotalCost = costPrice * holdingQty;
                decimal newCostPrice = (originalTotalCost - expectedProfit) / holdingQty;

                NewCostPriceTextBlock.Text = $"{Math.Round(newCostPrice, 3)} 元";
            }
            else
            {
                HoldingPnLTextBlock.Text = "---";
                HoldingPnLTextBlock.Foreground = Brushes.White;
                NewCostPriceTextBlock.Text = "---";
            }
        }
    }
}