using System;
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
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : Window
    {
        // 固定的过户费率（双向收取），十万分之1
        private const decimal TransferFeeRate = 0.00001m;

        public MainWindow()
        {
            InitializeComponent();
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
                if (!decimal.TryParse(TargetProfitTextBox.Text, out decimal targetProfit) || targetProfit < 0)
                    throw new Exception("预期净利必须为大于等于0的数字");

                bool isPositiveT = OperationModeComboBox.SelectedIndex == 0; // 0为正T，1为倒T
                bool isFree5 = IsFree5CheckBox.IsChecked == true;

                // 2. 调用核心计算逻辑
                CalculateBreakeven(price, quantity, commissionRate, stampDutyRate, isPositiveT, isFree5, targetProfit);
            }
            catch (Exception ex)
            {
                ErrorTextBlock.Text = $"输入错误：{ex.Message}";
                ErrorTextBlock.Visibility = Visibility.Visible;
                TotalFeeTextBlock.Text = "--- 元";
                TargetPriceTextBlock.Text = "--- 元";
                if (ProfitTargetPriceTextBlock != null) ProfitTargetPriceTextBlock.Text = "--- 元";
                if (HoldingPnLTextBlock != null) HoldingPnLTextBlock.Text = "---";
                if (NewCostPriceTextBlock != null) NewCostPriceTextBlock.Text = "---";
            }
        }

        /// <summary>
        /// 核心计算逻辑：计算保本差价和纯利目标
        /// </summary>
        private void CalculateBreakeven(decimal p, int n, decimal commissionRate, decimal stampDutyRate, bool isPositiveT, bool isFree5, decimal targetProfit)
        {
            // 初步估算：假设两笔交易价格相近，以首笔金额为准估算双边费率
            decimal totalTurnover = p * n; 

            // 计算单次佣金
            decimal singleCommission = totalTurnover * commissionRate;
            if (!isFree5 && singleCommission < 5m)
            {
                singleCommission = 5m;
            }

            // 买入手续费 = 佣金 + 过户费
            decimal buyFee = singleCommission + totalTurnover * TransferFeeRate;
            
            // 卖出手续费 = 佣金 + 过户费 + 印花税
            decimal sellFee = singleCommission + totalTurnover * TransferFeeRate + totalTurnover * stampDutyRate;

            // 总手续费
            decimal totalFee = buyFee + sellFee;

            // 保本所需每股差价
            decimal breakevenDiff = totalFee / n;
            // 达成纯利所需额外每股差价
            decimal profitDiff = targetProfit / n;

            // 目标价计算
            decimal targetPrice;
            decimal profitTargetPrice;

            if (isPositiveT)
            {
                // 正T(先买后卖)：卖出价需要 > 买入价 + 差价
                targetPrice = p + breakevenDiff;
                profitTargetPrice = p + breakevenDiff + profitDiff;
            }
            else
            {
                // 倒T(先卖后买)：买入价需要 < 卖出价 - 差价
                targetPrice = p - breakevenDiff;
                profitTargetPrice = p - breakevenDiff - profitDiff;
            }

            // 更新UI结果显示
            TotalFeeTextBlock.Text = $"{Math.Round(totalFee, 2)} 元";
            TargetPriceTextBlock.Text = $"{Math.Round(targetPrice, 3)} 元";
            ProfitTargetPriceTextBlock.Text = $"{Math.Round(profitTargetPrice, 3)} 元";

            // 进行持仓扩展分析
            CalculateHoldingPnL(p, n, targetProfit);
        }

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