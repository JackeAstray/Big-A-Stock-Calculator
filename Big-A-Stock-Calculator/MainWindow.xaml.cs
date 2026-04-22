using System;
using System.IO;
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

        public MainWindow()
        {
            InitializeComponent();
            LoadSettings();
            this.Closing += MainWindow_Closing;
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
            catch { /* 加载失败使用默认值 */ }
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
            catch { /* 保存失败忽略 */ }
        }

        private void MainWindow_Closing(object sender, System.ComponentModel.CancelEventArgs e)
        {
            SaveSettings();
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
                if (!decimal.TryParse(PriceTextBox.Text, out decimal price) || price <= 0)
                    throw new Exception("交易单价必须为大于0的数字");
                if (!int.TryParse(QuantityTextBox.Text, out int quantity) || quantity <= 0 || quantity % 100 != 0)
                    throw new Exception("交易股数必须为100的整数倍");
                if (!decimal.TryParse(TargetProfitTextBox.Text, out decimal targetProfitInput) || targetProfitInput < 0)
                    throw new Exception("预期盈利必须为大于等于0的数字");
                if (!decimal.TryParse(MaxLossTextBox.Text, out decimal maxLoss) || maxLoss < 0)
                    throw new Exception("最大最大承受亏损必须为大于等于0的数字");

                bool isPositiveT = OperationModeComboBox.SelectedIndex == 0; // 0为正T，1为倒T
                bool isFree5 = IsFree5CheckBox.IsChecked == true;

                decimal targetProfit = targetProfitInput;
                if (ProfitModeComboBox.SelectedIndex == 1) // 按收益率
                {
                    targetProfit = (price * quantity) * (targetProfitInput / 100m);
                }

                // 倒T逻辑防呆校验：校验是不是数量超卖
                if (!isPositiveT)
                {
                    if (int.TryParse(HoldingQuantityTextBox.Text, out int holdingQty) && quantity > holdingQty)
                    {
                        throw new Exception("倒T为先卖后买，交易卖出股数不能大于当前的【持有股份总数】！");
                    }
                }

                // 2. 调用核心计算逻辑
                CalculateBreakeven(price, quantity, commissionRate, stampDutyRate, isPositiveT, isFree5, targetProfit, maxLoss);
            }
            catch (Exception ex)
            {
                ErrorTextBlock.Text = $"输入错误：{ex.Message}";
                ErrorTextBlock.Visibility = Visibility.Visible;
                TotalFeeTextBlock.Text = "--- 元";
                if (FeeDetailTextBlock != null) FeeDetailTextBlock.Text = "(明细：佣金 --- 元 , 印花税 --- 元 , 过户费 --- 元)";
                TargetPriceTextBlock.Text = "--- 元";
                if (ProfitTargetPriceTextBlock != null) ProfitTargetPriceTextBlock.Text = "--- 元";
                if (StopLossPriceTextBlock != null) StopLossPriceTextBlock.Text = "--- 元";
                if (HoldingPnLTextBlock != null) HoldingPnLTextBlock.Text = "---";
                if (NewCostPriceTextBlock != null) NewCostPriceTextBlock.Text = "---";
            }
        }

        /// <summary>
        /// 核心计算逻辑：计算保本差价和纯利目标
        /// </summary>
        private void CalculateBreakeven(decimal p, int n, decimal commissionRate, decimal stampDutyRate, bool isPositiveT, bool isFree5, decimal targetProfit, decimal maxLoss)
        {
            // 初步估算：假设两笔交易价格相近，以首笔金额为准估算双边费率
            decimal totalTurnover = p * n; 

            // 计算单次佣金
            decimal singleCommission = totalTurnover * commissionRate;
            if (!isFree5 && singleCommission < 5m)
            {
                singleCommission = 5m;
            }

            // 【明细计算】
            decimal buyTransferFee = totalTurnover * TransferFeeRate;
            decimal buyFee = singleCommission + buyTransferFee;
            
            decimal sellTransferFee = buyTransferFee;
            decimal sellStampDuty = totalTurnover * stampDutyRate;
            decimal sellFee = singleCommission + sellTransferFee + sellStampDuty;

            decimal totalCommission = singleCommission * 2;
            decimal totalTransferFee = buyTransferFee + sellTransferFee;
            decimal totalStampDuty = sellStampDuty;

            // 总手续费
            decimal totalFee = buyFee + sellFee;

            // 保本所需每股差价
            decimal breakevenDiff = totalFee / n;
            // 达成纯利所需额外每股差价
            decimal profitDiff = targetProfit / n;
            // 最大亏损允许的差价
            decimal lossDiff = maxLoss / n;

            // 目标价计算
            decimal targetPrice;
            decimal profitTargetPrice;
            decimal stopLossPrice;

            if (isPositiveT)
            {
                // 正T(先买后卖)：卖出价需要 > 买入价 + 差价
                targetPrice = p + breakevenDiff;
                profitTargetPrice = p + breakevenDiff + profitDiff;
                stopLossPrice = p + breakevenDiff - lossDiff;
            }
            else
            {
                // 倒T(先卖后买)：买入价需要 < 卖出价 - 差价
                targetPrice = p - breakevenDiff;
                profitTargetPrice = p - breakevenDiff - profitDiff;
                stopLossPrice = p - breakevenDiff + lossDiff;
            }

            // 更新UI结果显示
            TotalFeeTextBlock.Text = $"{Math.Round(totalFee, 2)} 元";
            if (FeeDetailTextBlock != null)
                FeeDetailTextBlock.Text = $"(明细：佣金 {Math.Round(totalCommission, 2)} 元 , 印花税 {Math.Round(totalStampDuty, 2)} 元 , 过户费 {Math.Round(totalTransferFee, 2)} 元)";
            TargetPriceTextBlock.Text = $"{Math.Round(targetPrice, 3)} 元";
            ProfitTargetPriceTextBlock.Text = $"{Math.Round(profitTargetPrice, 3)} 元";
            if (StopLossPriceTextBlock != null) StopLossPriceTextBlock.Text = $"{Math.Round(stopLossPrice, 3)} 元";

            // 进行持仓扩展分析
            CalculateHoldingPnL(p, n, targetProfit);
        }

        // ================ 各个额外快捷按钮事件 ================
        
        private void ResetButton_Click(object sender, RoutedEventArgs e)
        {
            ErrorTextBlock.Visibility = Visibility.Collapsed;
            PriceTextBox.Text = "";
            QuantityTextBox.Text = "";
            TargetProfitTextBox.Text = "100";
            MaxLossTextBox.Text = "100";
            YesterdayCloseTextBox.Text = "";
            LimitUpTextBlock.Text = "---";
            LimitDownTextBlock.Text = "---";
            
            CostPriceTextBox.Text = "";
            HoldingQuantityTextBox.Text = "";
            CurrentPriceTextBox.Text = "";
            
            TotalFeeTextBlock.Text = "--- 元";
            if (FeeDetailTextBlock != null) FeeDetailTextBlock.Text = "(明细：佣金 --- 元 , 印花税 --- 元 , 过户费 --- 元)";
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
            string mode = OperationModeComboBox.SelectedIndex == 0 ? "正T" : "倒T";
            string record = $"[{DateTime.Now:HH:mm:ss}] {mode} | 价:{PriceTextBox.Text} 股:{QuantityTextBox.Text} | 目标:{ProfitTargetPriceTextBlock.Text} 止损:{StopLossPriceTextBlock.Text}";
            HistoryListBox.Items.Insert(0, record);
        }

        private void ClearHistoryButton_Click(object sender, RoutedEventArgs e)
        {
            HistoryListBox.Items.Clear();
        }

        private void FillQuantity(double fraction)
        {
            if (int.TryParse(HoldingQuantityTextBox.Text, out int holdingQty) && holdingQty > 0)
            {
                int targetQty = (int)(holdingQty * fraction);
                targetQty = (targetQty / 100) * 100; // 向下取整到100的倍数
                if (targetQty > 0)
                {
                    QuantityTextBox.Text = targetQty.ToString();
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

        private void BtnFullPosition_Click(object sender, RoutedEventArgs e) => FillQuantity(1.0);
        private void BtnHalfPosition_Click(object sender, RoutedEventArgs e) => FillQuantity(0.5);
        private void BtnOneThirdPosition_Click(object sender, RoutedEventArgs e) => FillQuantity(1.0 / 3.0);


        /// <summary>
        /// 分析做T完成后的持仓和成本变化
        /// </summary>
        private void CalculateHoldingPnL(decimal p, int n, decimal expectedProfit)
        {
            if (decimal.TryParse(CostPriceTextBox.Text, out decimal costPrice) && costPrice > 0 &&
                int.TryParse(HoldingQuantityTextBox.Text, out int holdingQty) && holdingQty >= n)
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
                HoldingPnLTextBlock.Foreground = currentPnL > 0 ? Brushes.Red : (currentPnL < 0 ? Brushes.Green : Brushes.Black);

                // 核心指标：一旦完成这波 T 以后，新成本被摊薄为多少
                // 原总成本 - 赚到的净利润现金 = 新的总成本。 除以总股数即为新成本价
                decimal originalTotalCost = costPrice * holdingQty;
                decimal newCostPrice = (originalTotalCost - expectedProfit) / holdingQty;

                NewCostPriceTextBlock.Text = $"{Math.Round(newCostPrice, 3)} 元";
            }
            else
            {
                HoldingPnLTextBlock.Text = "---";
                HoldingPnLTextBlock.Foreground = Brushes.Black;
                NewCostPriceTextBlock.Text = "---";
            }
        }
    }
}