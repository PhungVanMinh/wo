using OneSource.App.Dapper;
using OneSource.App.Entities;
using OneSource.App.Forms.Data;
using OneSource.App.Forms.MyUserControl;
using OneSource.App.Helper;
using OneSource.App.Repositories;
using OneSource.App.Shared;
using OneSource.App.Shared.Components;
using OneSource.App.Shared.UserControls;
using OneSource.App.TemplateReports;
using OneSource.Common;
using OneSource.Common.EventArgs;
using OneSource.Common.Exceptions;
using OneSource.Common.Extensions;
using OneSource.Common.Resources;
using OneSource.Common.Utils;
using OneSource.Services;
using OneSource.Services.FormServices.Business;
using OneSource.Services.FormServices.Data;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;
using Telerik.Reporting.Processing;
using Telerik.WinControls;
using Telerik.WinControls.UI;
using static OneSource.App.Helper.CustomEntity;

namespace OneSource.App.Forms.Business
{
    public partial class FrmWorkOrderPart : BaseRadForm
    {

        private readonly Tuple<decimal, string>[] _companyLaborRates = new Tuple<decimal, string>[3];
        private ConcernPanelControl _concernPanelControl;
        private VinInformation _currentVehicle;
        private CommercialCustomer _customer;
        private int _customerId;
        private List<Employee> _lstTechnicians;
        private List<CustomData> _lstWorkOrderStatus;
        private dynamic _oldVehicle;
        private OneSourceAlert _oneSourceAlert;
        private decimal _totalSalesTaxCompany;
        private ValidationProvider _validationProvider;
        private int _vehicleId;
        private WorkOrder _workOrderEntityResult;
        private string _workOrderNumber;
        private bool btnAddAdditionalConcernEnabled;
        private bool btnAddPartsLaborEnabled;
        private bool btnAddSubletEnabled;
        private bool btnAddTowEnabled;
        private bool btnCarIsReadyTextEnabled;
        private bool btnCloseWOEnabled;
        private bool btnAddCannedJobEnabled;
        private bool btnConvertWOEnabled;
        private bool btnDeferEnabled;
        private bool btnHistoryEnabled;
        private bool btnOrderPartsEnabled;
        private bool btnPartListEnabled;
        private bool btnPaymentsEnabled;
        private bool btnPleaseCallTextEnabled;
        private bool btnEstimateEnabled;
        private bool btnPricePartsEnabled;
        private bool btnReceiveOrderPartEnabled;
        private bool btnSaveEnabled;
        private bool btnUnDeferedEnabled;
        private VinInforAutocompleteHandler vinInforHandler = null;
        private string workOrderType = "";

        bool _isPayment;
        public FrmWorkOrderPart(List<WorkorderNotification> workorderNotification = null, bool isPayment = false) : base(true)
        {
            InitializeComponent();
            InitControl();
            Initialization();
            FormHelper.AlignProcessBar(wBSearching, Size.Width, Size.Height);

            Size = new Size(Screen.PrimaryScreen.WorkingArea.Width, Screen.PrimaryScreen.WorkingArea.Height);
            tbCustomInfor.Visible = panelInvoice.Visible =
               (PersistentModels.CurrentUser.UserGroup.ToUpper() != UserGroupRole.TECHNICIAN.ToString());

            lblFinishTime.Text = "";
            _isPayment = isPayment;
        }

        public decimal _saleTaxTotal { get; set; }
        public int _workOrderId { get; set; }
        public bool IsExemption { get; set; }
        private CommercialCustomer _currentCustomer { get; set; }
        private decimal AmountDiscount { get; set; }
        private decimal FEIAmount { get; set; }
        private bool IsAvailabeCloseWorkOrder { get; set; }
        private decimal TotalLabors { get; set; }
        private decimal TotalParts { get; set; }
        private decimal TotalSublet { get; set; }
        private decimal TotalTowing { get; set; }
        private string woToken { get; set; }
        public bool ParseValidationControlsMapping(bool isError, ErrorCode errorCode, string message)
        {
            _validationProvider.IsShowTooltip = false;
            _validationProvider.IsFocus = false;
            ResetValidation();

            if (isError)
            {
                switch (errorCode)
                {
                    case ErrorCode.YearRequired:
                        _validationProvider.SetError(txtVinYear, message);
                        break;

                    case ErrorCode.MakeRequired:
                        _validationProvider.SetError(txtVinMake, message);
                        break;

                    case ErrorCode.ModelRequired:
                        _validationProvider.SetError(txtVinModel, message);
                        break;

                    case ErrorCode.WoStatusRequired:
                        _validationProvider.SetError(drdWorkOrderStatus, message);
                        break;

                    case ErrorCode.MileRequired:
                        _validationProvider.SetError(txtVinMiles, message);
                        break;

                    default:
                        throw new OneSourceException(ErrorMessage.SaveDataFailed + ": " + message);
                }
                return false;
            }
            return true;
        }

        public void ReloadConcerns(string woStatusNm = "")
        {
            wBSearching.Text = "Reloading data ...";
            wBSearching.Visible = true;
            wBSearching.StartWaiting();
            AsynsLoadConcern(woStatusNm);
        }

        decimal? _totalPart = 0;
        List<sumPart> sumPartLst = new List<sumPart>();
        public void SumTotal(decimal? totalPart, int concernId, bool isConcernClick = false)
        {
            panelSumPart.Visible = true;
            if (totalPart != null)
            {
                var concernCount = sumPartLst.Where(p => p.ConcernId == concernId).Count();
                if (concernCount == 0)
                {
                    sumPartLst.Add(new sumPart { TotalPart = totalPart, ConcernId = concernId });
                }
                else
                {
                    sumPartLst = sumPartLst.Where(p => p.ConcernId != concernId).ToList();
                }
                _totalPart = sumPartLst.Sum(p => p.TotalPart);

                lbTotalPart.Text = "TOTAL HIGHLIGHTED JOB: " + _totalPart?.ToString("C");
                if (_totalPart == 0)
                {
                    panelSumPart.Visible = false;
                    sumPartLst = new List<sumPart>();
                }
            }
        }
        class sumPart
        {
            public decimal? TotalPart;
            public int ConcernId;
        }

        public void ResetValidation()
        {
            _validationProvider.Reset(txtVinNumber, txtVinTag, txtVinYear, txtVinMake, txtVinModel);
        }

        public async void SetAmountDiscount(decimal discountAmount, decimal discountPercentage)
        {
            AmountDiscount = discountAmount;
            var changedResult = await
            FrmWorkOrderPartService.CreateService().SPWorkOrderRepositoryUpdateAmountDiscountAsync(new WorkOrder
            {
                WorkOrderId = PersistentModels.CurrentWorkOrderId,
                AmountDiscount = AmountDiscount,
                DiscountPercentage = discountPercentage
            });


            if (changedResult.IsError) throw new OneSourceException(changedResult.Message);
            CalculateTotal();
        }

        private void ShowHideSummary(bool isVisible)
        {
            //tbMisc.Visible = isVisible;
            //tabPartLabor.Visible = isVisible;
            //tableTotal.Visible = isVisible;
        }

        private async void AsynsLoadConcern(string woStatusNm = "")
        {
            panelGrid.Visible = false;

            var workOrderEntityResult = await FrmWorkOrderPartService.CreateService().GetByIdAsync(PersistentModels.CurrentWorkOrderId);

            _concernPanelControl.ReloadConcernData(workOrderEntityResult?.Concerns, _companyLaborRates, PersistentModels.CurrentCompany);
            CalculateDeferButtonStatus();
            CalculateTotal();
            if (!string.IsNullOrEmpty(woStatusNm))
            {
                if (woStatusNm.ToLower() == WorkOrderStatusEnum.CLOSED.ToString().ToLower())
                {
                    drdWorkOrderStatus.Text = WorkOrderStatusEnum.CLOSED.ToString();
                    drdWorkOrderStatus.ReadOnly = true;
                }
                else
                {
                    drdWorkOrderStatus.SelectedValue = woStatusNm;
                }
            }
            panelGrid.Visible = true;
            wBSearching.Visible = false;
            wBSearching.StopWaiting();
            wBSearching.Text = "Loading, please wait ...";
        }

        private void CalculateDeferButtonStatus()
        {
            btnDefer.Enabled = _concernPanelControl.LstConcernControls.Any(x => (x.IsConcernSelected && !x.Concern.IsDefer) || (x.ListLaborGuideTitle.Any(lb => lb.IsSelected && !lb.IsDefered)));
            btnUnDefered.Enabled = _concernPanelControl.LstConcernControls.Any(x => (x.IsConcernSelected && x.Concern.IsDefer) || (x.ListLaborGuideTitle.Any(lb => lb.IsSelected && lb.IsDefered)));
        }

        private async void CalculateTotal()
        {
            ShowHideSummary(false);

            WorkOrder woSummary = null;
            if (_workOrderEntityResult.WorkOrderStatusName.ToLower() == WorkOrderStatusEnum.CLOSED.ToString().ToLower())
            {
                woSummary = _workOrderEntityResult;
            }
            else
            {
                woSummary = await FrmWorkOrderPartService.CreateService().SummaryWO(PersistentModels.CurrentWorkOrderId);
            }
            TotalLabors = woSummary.AmountLabor.Value;
            TotalParts = woSummary.AmountPart.Value;
            TotalSublet = woSummary.AmountSublet == null ? 0 : woSummary.AmountSublet.Value;
            TotalTowing = woSummary.AmountTowing.Value;
            _saleTaxTotal = woSummary.SaleTax.Value;

            lblLaborTotal.Text = TotalLabors.ToStringDecimal();
            lblPartsTotal.Text = TotalParts.ToStringDecimal();
            lblSublet.Text = TotalSublet.ToStringDecimal();
            lblTowing.Text = TotalTowing.ToStringDecimal();


            lblTotal.Text = woSummary.AmountAfterTax.Value.ToStringDecimal();
            lblFEITaxTotal.Text = FEIAmount.ToStringDecimal();
            lblDiscountTotal.Text = AmountDiscount.ToStringDecimal();

            lblShopCharge.Text = woSummary.ShopCharge.Value.ToStringDecimal();
            lblCreditCard.Text = woSummary.CreditCardCharge.ToStringDecimal();
            //var creditCardCharge = Convert.ToDecimal(lblCreditCardConv.Text);
            lblSalesTaxTotal.Text = _saleTaxTotal.ToStringDecimal();

            lblAMTPaidTotal.Text = woSummary.AmountPaid.Value.ToStringDecimal();
            lblTotalDue.Text = woSummary.TotalDue.Value.ToStringDecimal();

            Size size = TextRenderer.MeasureText(lblTotalDue.Text, lblTotalDue.Font);
            lblTotalDue.Size = size;

            var lateCharge = 0.0M;
            if (_workOrderEntityResult.FinishDate != null && _workOrderEntityResult.WorkOrderStatusName != WorkOrderStatusEnum.CLOSED.ToString())
            {
                TimeSpan diffDate = DateTime.Now - _workOrderEntityResult.FinishDate.Value;
                int lateNo = diffDate.Days;
                if (lateNo > PersistentModels.CurrentCompany.ChargesAccumulateAfterDays)
                {
                    lateCharge = (lateNo - PersistentModels.CurrentCompany.ChargesAccumulateAfterDays)
                        * PersistentModels.CurrentCompany.StorageChargePerDay;
                }
            }
            lblLateCharge.Text = lateCharge.ToStringDecimal();

            ResetTable();
            float rH = 25.7247f;// this.tableTotal.Size.Height * (float)((float)9.09 / 100);

            tabPartLabor.Controls.Clear();
            {
                int i = 0;
                if (Convert.ToDecimal(lblLaborTotal.Text.Replace("$", "")) != 0)
                {
                    tabPartLabor.Controls.Add(radLabel5, 1, i);
                    tabPartLabor.Controls.Add(radLabel19, 0, i);
                    tabPartLabor.Controls.Add(lblLaborTotal, 2, i);
                    i++;
                }

                if (Convert.ToDecimal(lblPartsTotal.Text.Replace("$", "")) != 0)
                {
                    tabPartLabor.Controls.Add(radLabel41, 1, i);
                    tabPartLabor.Controls.Add(radLabel40, 0, i);
                    tabPartLabor.Controls.Add(lblPartsTotal, 2, i);
                    i++;
                }

                int remainderItems = tabPartLabor.RowCount - i;

                for (int j = i; j < tabPartLabor.RowStyles.Count; j++)
                {
                    tabPartLabor.RowStyles.RemoveAt(j);
                }
                //this.tabPartLabor.RowCount = this.tabPartLabor.RowCount - remainderItems;

                var tbHeight = tabPartLabor.Size.Height;
                int tabH = tbHeight - (int)(remainderItems * rH);
                tabPartLabor.Size = new Size(197, tabH);
            }

            var miscLst = FrmWorkOrderPartService.CreateService().GetUnitOfWork().workOrderRepository
                .GetMisc(PersistentModels.CurrentWorkOrderId);
            tbMisc.Controls.Clear();
            if (tbMisc.RowStyles.Count > 0)
            {
                tbMisc.RowStyles.RemoveAt(0);
            }
            if (tbMisc.RowStyles.Count > 0)
            {
                tbMisc.RowStyles.RemoveAt(0);
            }

            tbMisc.RowCount = miscLst.Count;
            for (int r = 0; r < miscLst.Count; r++)
            {
                var lbl0 = new RadLabel();
                lbl0.Font = new Font("Segoe UI", 8.25F, FontStyle.Bold);
                lbl0.Dock = DockStyle.Right;
                lbl0.Text = miscLst[r].MiscName;
                var lbl1 = new RadLabel();
                lbl1.Font = new Font("Segoe UI", 8.25F, FontStyle.Bold);
                lbl1.Dock = DockStyle.Fill;
                lbl1.Text = "$";
                var lbl2 = new RadLabel();
                lbl2.Font = new Font("Segoe UI", 8.25F, FontStyle.Bold);
                lbl2.Dock = DockStyle.Right;
                lbl2.Text = miscLst[r].MiscAmount.ToStringDecimal();

                tbMisc.Controls.Add(lbl0, 0, r);
                tbMisc.Controls.Add(lbl1, 1, r);
                tbMisc.Controls.Add(lbl2, 2, r);

                tbMisc.RowStyles.Add(new RowStyle(SizeType.Absolute, rH));
            }
            tbMisc.Size = new Size(197, (int)(tbMisc.RowCount * rH));
            tbMisc.Location = new Point(0, tabPartLabor.Location.Y + tabPartLabor.Size.Height);

            tableTotal.Controls.Clear();
            {
                int i = 0;

                if (Convert.ToDecimal(lblSublet.Text.Replace("$", "")) != 0)
                {
                    tableTotal.Controls.Add(radLabel23, 1, i);
                    tableTotal.Controls.Add(lblSublet, 2, i);
                    tableTotal.Controls.Add(radLabel14, 0, i);
                    i++;
                }

                if (Convert.ToDecimal(lblTowing.Text.Replace("$", "")) != 0)
                {
                    tableTotal.Controls.Add(radLabel25, 1, i);
                    tableTotal.Controls.Add(radLabel17, 0, i);
                    tableTotal.Controls.Add(lblTowing, 2, i);
                    i++;
                }

                if (Convert.ToDecimal(lblFEITaxTotal.Text.Replace("$", "")) != 0)
                {
                    tableTotal.Controls.Add(radLabel27, 1, i);
                    tableTotal.Controls.Add(radLabel26, 0, i);
                    tableTotal.Controls.Add(lblFEITaxTotal, 2, i);
                    i++;
                }



                if (Convert.ToDecimal(lblShopCharge.Text.Replace("$", "")) != 0)
                {
                    tableTotal.Controls.Add(radLabel30, 1, i);
                    tableTotal.Controls.Add(radLabel13, 0, i);
                    tableTotal.Controls.Add(lblShopCharge, 2, i);
                    i++;
                }
                if (Convert.ToDecimal(lblCreditCard.Text.Replace("$", "")) != 0)
                {
                    tableTotal.Controls.Add(radLabel31, 1, i);
                    tableTotal.Controls.Add(radLabel46, 0, i);
                    tableTotal.Controls.Add(lblCreditCard, 2, i);
                    i++;
                }

                if (Convert.ToDecimal(lblDiscountTotal.Text.Replace("$", "")) != 0)
                {
                    tableTotal.Controls.Add(radLabel33, 1, i);
                    tableTotal.Controls.Add(lblDiscountTotal, 2, i);
                    tableTotal.Controls.Add(radLabel9, 0, i);
                    i++;
                }

                if (Convert.ToDecimal(lblLateCharge.Text.Replace("$", "")) != 0)
                {
                    tableTotal.Controls.Add(radLabel34, 1, i);
                    tableTotal.Controls.Add(radLabel15, 0, i);
                    tableTotal.Controls.Add(lblLateCharge, 2, i);
                    i++;
                }
                if (Convert.ToDecimal(lblSalesTaxTotal.Text.Replace("$", "")) != 0)
                {
                    tableTotal.Controls.Add(radLabel47, 1, i);
                    tableTotal.Controls.Add(radLabel22, 0, i);
                    tableTotal.Controls.Add(lblSalesTaxTotal, 2, i);
                    i++;
                }
                if (Convert.ToDecimal(lblTotal.Text.Replace("$", "")) != 0)
                {
                    tableTotal.Controls.Add(radLabel35, 1, i);
                    tableTotal.Controls.Add(lblTotal, 2, i);
                    tableTotal.Controls.Add(radLabel32, 0, i);
                    i++;
                }

                if (Convert.ToDecimal(lblAMTPaidTotal.Text.Replace("$", "")) != 0)
                {
                    tableTotal.Controls.Add(radLabel36, 1, i);
                    tableTotal.Controls.Add(radLabel24, 0, i);
                    tableTotal.Controls.Add(lblAMTPaidTotal, 2, i);
                    i++;
                }

                if (Convert.ToDecimal(lblTotalDue.Text.Replace("$", "")) != 0)
                {
                    tableTotal.Controls.Add(radLabel37, 1, i);
                    tableTotal.Controls.Add(radLabel11, 0, i);
                    tableTotal.Controls.Add(lblTotalDue, 2, i);
                    i++;
                }

                int remainderItems = tableTotal.RowCount - i;

                for (int j = i; j < tableTotal.RowStyles.Count; j++)
                {
                    tableTotal.RowStyles.RemoveAt(j);
                }

                tableTotal.RowCount = tableTotal.RowCount - remainderItems;

                var tbHeight = tableTotal.Size.Height;
                int tabH = tbHeight - (int)(remainderItems * rH);
                tableTotal.Size = new Size(197, tabH);
                tableTotal.Location = new Point(0, tbMisc.Location.Y + tbMisc.Size.Height);
            }
            ShowHideSummary(true);

        }

        void ResetTable()
        {
            tabPartLabor.RowStyles.Clear();
            tabPartLabor.Size = new Size(209, 48);
            tabPartLabor.RowCount = 2;
            tabPartLabor.RowStyles.Add(new RowStyle(SizeType.Percent, 50F));
            tabPartLabor.RowStyles.Add(new RowStyle(SizeType.Percent, 50F));

            tableTotal.RowStyles.Clear();
            tableTotal.Size = new Size(209, 283);
            tableTotal.RowCount = 11;
            tableTotal.RowStyles.Add(new RowStyle(SizeType.Percent, 9.090909F));
            tableTotal.RowStyles.Add(new RowStyle(SizeType.Percent, 9.090909F));
            tableTotal.RowStyles.Add(new RowStyle(SizeType.Percent, 9.090909F));
            tableTotal.RowStyles.Add(new RowStyle(SizeType.Percent, 9.090909F));
            tableTotal.RowStyles.Add(new RowStyle(SizeType.Percent, 9.090909F));
            tableTotal.RowStyles.Add(new RowStyle(SizeType.Percent, 9.090909F));
            tableTotal.RowStyles.Add(new RowStyle(SizeType.Percent, 9.090909F));
            tableTotal.RowStyles.Add(new RowStyle(SizeType.Percent, 9.090909F));
            tableTotal.RowStyles.Add(new RowStyle(SizeType.Percent, 9.090909F));
            tableTotal.RowStyles.Add(new RowStyle(SizeType.Percent, 9.090909F));
            tableTotal.RowStyles.Add(new RowStyle(SizeType.Percent, 9.090909F));
        }
        private async Task CheckAndProcessWorkOrder(DateTime closedTime, int? serviceWriterId, int? miles)
        {
            string uploadedReceipt = _workOrderEntityResult.ReceiptUrl;
            if (string.IsNullOrWhiteSpace(uploadedReceipt))
            {
                uploadedReceipt = await UploadReceipt();
            }

            var changedResult = await FrmWorkOrderPartService.CreateService().SPWorkOrderRepositoryChangeStatusAndUpdateInfoAsync(new WorkOrder
            {
                WorkOrderId = PersistentModels.CurrentWorkOrderId,
                WorkOrderStatusName = WorkOrderStatusEnum.CLOSED.ToString(),
                ReceiptUrl = uploadedReceipt,
                ClosedTime = closedTime,
                ServiceWriterId = serviceWriterId,
                Miles = miles
            });

            if (changedResult.IsError)
            {
                wBSearching.StopWaiting();
                wBSearching.Visible = false;
                throw new OneSourceException(changedResult.Message);
            }
        }

        private void CloseFrm()
        {
            Close();
        }

        private async void CloseWorkOrder()
        {

            if (!CloseWoValidation())
            {
                return;
            }

            var partNoReceivedCount = FrmWorkOrderPartService.CreateService().SPPartOrderRepositoryPartNotReceivedCountAsync(PersistentModels.CurrentWorkOrderId);
            if (partNoReceivedCount > 0)
            {
                var popupSpecialOrder = new PopupSpecialOrder();
                popupSpecialOrder.ShowDialog();
                if (!popupSpecialOrder.IsSpecial)
                {
                    return;
                }
            }

            var frm = new FrmCloseWorkOrder();
            frm.ShowDialog();


            try
            {
                wBSearching.StartWaiting();
                wBSearching.Visible = true;
                wBSearching.Text = @"CLOSING WORK ORDER";

                _validationProvider.Reset(txtVinMiles, drdServiceWtr);
                var vinInfor = new VinInformation();
                vinInfor.Tag = txtVinTag.Text;
                vinInfor.Miles = txtVinMiles.Value.GetActualNullableInteger();
                vinInfor.VinId = _currentVehicle.VinId;
                int.TryParse(txtVinYear.Text, out var vinYear);
                vinInfor.ModelYear = vinYear;
                vinInfor.Make = txtVinMake.Text;
                vinInfor.Model = txtVinModel.Text;
                FrmWorkOrderPartService.CreateService().SPWorkOrderRepositoryUpdateVinInforAsync(vinInfor);


                await CheckAndProcessWorkOrder(frm.CloseTime, Convert.ToInt32(drdServiceWtr.SelectedValue), txtVinMiles.Value.GetActualNullableInteger());

                await FrmWorkOrderPartService.CreateService().SPWorkOrderRepositoryUpdateStockAsync(PersistentModels.CurrentWorkOrderId);


                await OSRest.ReactVinInfor(new
                {
                    Email = _currentCustomer.Email,
                    vehicle = new
                    {
                        year = Convert.ToInt16(txtVinYear.Text),
                        make = txtVinMake.Text,
                        model = txtVinModel.Text,
                        vinNumber = txtVinNumber.Text
                    },
                    status = "CLOSED",
                    shop = PersistentModels.ShopName,
                    workOrderId = _workOrderId,
                    workOrderNumber = Convert.ToInt32(_workOrderNumber),
                    customer = new
                    {
                        email = _currentCustomer.Email,
                        firstName = _currentCustomer.FirstName,
                        lastName = _currentCustomer.LastName,
                    }
                });

                var notification = new Notification
                {
                    FirstName = _customer.FirstName.ToLower(),
                    LastName = _customer.LastName.ToLower(),
                    Year = txtVinYear.Text,
                    Make = txtVinMake.Text,
                    Model = txtVinModel.Text,
                };

                await ProcessContent(notification);
            }
            finally
            {
                wBSearching.StopWaiting();
                wBSearching.Visible = false;
            }

            Close();
        }

        private bool CloseWoValidation()
        {
            if (string.IsNullOrWhiteSpace(PersistentModels.CurrentCompany.ReviewContent))
            {
                RadMessageBox.Show("REVIEW CONTENT IS NOT SET UP. CONTACT ADMIN", "REVIEW CONTENT", MessageBoxButtons.OK, RadMessageIcon.Error);
                return false;
            }

            if (!IsRightAmount())
            {
                return false;
            }

            var unAssignedCount = FrmWorkOrderPartService.CreateService().SPconcernRepositoryGetUnAssignedConcernAsync(PersistentModels.CurrentWorkOrderId);

            if (unAssignedCount > 0)
            {
                RadMessageBox.Show("ALL JOBS MUST BE ASSIGNED BEFORE CLOSE", "JOB", MessageBoxButtons.OK, RadMessageIcon.Error);
                return false;
            }
            var vinInfor = new VinInformation();
            vinInfor.Miles = txtVinMiles.Value.GetActualNullableInteger();
            vinInfor.VinId = _currentVehicle.VinId;
            int vinYear;
            int.TryParse(txtVinYear.Text, out vinYear);
            vinInfor.ModelYear = vinYear;
            vinInfor.Make = txtVinMake.Text;
            vinInfor.Model = txtVinModel.Text;
            string message = "";
            ErrorCode errorCode = ErrorCode.YearRequired;
            if (vinYear == 0)
            {
                message = VinInformationMessage.YearRequired;
                errorCode = ErrorCode.YearRequired;
            }
            else if (string.IsNullOrEmpty(vinInfor.Make))
            {
                message = VinInformationMessage.MakeRequired;
                errorCode = ErrorCode.MakeRequired;
            }
            else if (string.IsNullOrEmpty(vinInfor.Model))
            {
                message = VinInformationMessage.ModelRequired;
                errorCode = ErrorCode.ModelRequired;
            }
            else if (drdWorkOrderStatus.SelectedValue == null || drdWorkOrderStatus.SelectedValue.ToString() == "0")
            {
                message = "Status is required";
                errorCode = ErrorCode.WoStatusRequired;
            }
            else if (txtVinMiles.Value.GetActualNullableInteger() == 0)
            {
                message = "MILES IS REQUIRED";
                errorCode = ErrorCode.MileRequired;
            }
            if (!string.IsNullOrEmpty(message))
            {
                ParseValidationControlsMapping(true, errorCode, message);
                return false;
            }

            if (drdServiceWtr.SelectedValue == null || drdServiceWtr.SelectedValue.ToString() == "0")
            {
                _validationProvider.SetError(drdServiceWtr, "WRITER SERVICE IS REQUIRED");
                RadMessageBox.Show("WRITER SERVICE IS REQUIRED", "WRITER SERVICE",
                    MessageBoxButtons.OK, RadMessageIcon.Error);
                return false;
            }
            var partWithoutPartNumber = FrmWorkOrderPartService.CreateService().SPWorkOrderRepositoryGetPartWithoutPartNumberAsync(PersistentModels.CurrentWorkOrderId);

            if (!string.IsNullOrEmpty(partWithoutPartNumber))
            {
                RadMessageBox.Show("YOU HAVE PART WITHOUT PARTNUMBER", "PART WITHOUT PARTNUMBER",
                                       MessageBoxButtons.OK, RadMessageIcon.Error);
                return false;
            }

            var noInvoiceSublet = FrmMainService.CreateService().GetUnitOfWork().workOrderRepository.VALIDATE_SUBLET_INVOICE(PersistentModels.CurrentWorkOrderId);
            if (noInvoiceSublet > 0)
            {
                RadMessageBox.Show("MISSING INVOICE IN SUBLET. PLEASE EDIT AND SCAN", "SUBLET VALIDATION", MessageBoxButtons.OK, RadMessageIcon.Error);
                return false;
            }

            var validSubletInfor = FrmMainService.CreateService().GetUnitOfWork().workOrderRepository.SP_CHECKSUBLET(PersistentModels.CurrentWorkOrderId);
            if (validSubletInfor > 0)
            {
                RadMessageBox.Show("SUBLET INFORMATION IS MISSING", "SUBLET VALIDATION", MessageBoxButtons.OK, RadMessageIcon.Error);
                return false;
            }

            return true;
        }

        private void ConcernPanelValueChanged(object sender, EventArgs e)
        {
            CalculateDeferButtonStatus();
        }
        private async void DeferConcern(List<int> lstSelectedConcerns, List<DeferedLaborGuideParameter> lstSelectedDeferedLaborGuides)
        {
            if (lstSelectedConcerns.Count > 0 || lstSelectedDeferedLaborGuides.Count > 0)
            {
                var result = await FrmWorkOrderPartService.CreateService().SPWorkOrderRepositoryInsertDeferedConcernAsync(new CreateDeferedConcernParameter
                {
                    WorkOrderId = PersistentModels.CurrentWorkOrderId,
                    ListConcernIds = lstSelectedConcerns,
                    ListDeferedLaborGuides = lstSelectedDeferedLaborGuides,
                    VehicleId = _vehicleId
                });

                _validationProvider.IsShowTooltip = false;
                _validationProvider.IsFocus = false;
                ResetValidation();
                if (result.IsError)
                {
                    ProcessResultError(result);
                }
                else
                {
                    ProcessResultOK(lstSelectedConcerns);
                }
            }
        }

        private void DeferUndeferConcern(object sender, ConcernEventArgs e)
        {
            Cursor.Current = Cursors.WaitCursor;
            if (!e.IsDefer)
            {
                DeferConcern(new List<int> { e.ConcernId }, new List<DeferedLaborGuideParameter>());
            }
            else
            {
                UnDeferConcern(new List<Entities.Concern>
                {
                    new Entities.Concern
                    {
                        ConcernId = e.ConcernId,
                        VinNumber = e.VinNumber,
                    }
                }, new List<UnDeferedLaborGuideParameter>());
            }
            Cursor.Current = Cursors.Default;
        }

        private void DeferUndeferLaborGuide(object sender, LaborGuideEventArgs e)
        {
            Cursor.Current = Cursors.WaitCursor;
            if (!e.IsDeferLaborGuide)
            {
                DeferConcern(new List<int>(), new List<DeferedLaborGuideParameter>
                {
                    new DeferedLaborGuideParameter
                    {
                        ConcernId = e.ConcernId,
                        LaborTimeId = e.LaborTimeId
                    }
                });
            }
            else
            {
                UnDeferConcern(new List<Entities.Concern>(), new List<UnDeferedLaborGuideParameter>
                {
                    new UnDeferedLaborGuideParameter
                    {
                        Concern = new Entities.Concern
                        {
                            ConcernId = e.ConcernId,
                        },
                        LaborTimeId = e.LaborTimeId
                    }
                });
            }
            Cursor.Current = Cursors.Default;
        }


        private void DisableControls(Control con)
        {
            foreach (Control c in con.Controls)
            {
                DisableControls(c);
            }
            if (con is RadButton)
            {
                con.Enabled = false;
            }
            btnOrderParts.Enabled = btnCloseWO.Enabled = false;
        }

        private void FrmWorkOrderPart_Shown(object sender, EventArgs e)
        {
            System.Windows.Forms.ToolTip tt = new System.Windows.Forms.ToolTip();
            tt.Show("RIGHT CLICK TO ADD DESCRIPTION FOR CONCERN", lblListConcerns, 40, 80, 5000);
            System.Windows.Forms.ToolTip ttDelete = new System.Windows.Forms.ToolTip();
            ttDelete.Show("RIGHT CLICK ON WO NUMBER TO DELETE", lblWorkOrderTitle, 30, 20, 5000);
            this.lblCompanyName.UseMnemonic = false;
        }
        private string GetAlternative()
        {
            _currentCustomer = _workOrderEntityResult.Customer;
            AmountDiscount = _workOrderEntityResult.AmountDiscount ?? 0M;
            FEIAmount = _workOrderEntityResult.FEIAmount ?? 0M;
            _workOrderNumber = _workOrderEntityResult.WorkOrderNumber.ToString();
            _workOrderId = _workOrderEntityResult.WorkOrderId;
            lblWorkOrderTitle.Text += string.Format(" {0} {1}", _workOrderEntityResult.WorkOrderNumber.ToString(), _workOrderEntityResult.CreatedDate.Value.ToString("MM/dd/yyyy HH:mm"));
            lblFirstName.Text = _workOrderEntityResult.Customer?.FirstName + @" " + _workOrderEntityResult?.Customer?.LastName;
            lblCompanyName.Text = _workOrderEntityResult.Customer?.CompanyName;
            lblCustomerAddress.Text = _workOrderEntityResult.Customer.Address;
            lblCustomerCity.Text = _workOrderEntityResult.Customer?.City + " " + _workOrderEntityResult?.Customer?.State + ", " + _workOrderEntityResult.Customer?.Zip;
            lblCustomerPhone1.Text = FormHelper.CalculateMaskedText(Constants.PhoneMaskValue, _workOrderEntityResult.Customer?.Phone1.GetActualDecimal().ToString(CultureInfo.InvariantCulture));
            lblFinishTime.Text = string.Format("{0:yyyy/MM/dd}", _workOrderEntityResult.FinishDate);
            string alternative = string.Empty;

            if (_workOrderEntityResult.Customer?.AltPhone != null && !string.IsNullOrEmpty(_workOrderEntityResult.Customer?.AltPhone.Trim()))
            {
                alternative = FormHelper.CalculateMaskedText(Constants.PhoneMaskValue, _workOrderEntityResult.Customer?.AltPhone.Trim().GetActualDecimal().ToString(CultureInfo.InvariantCulture));
            }

            if (_workOrderEntityResult.Customer?.AltFirst != string.Empty)
            {
                if (alternative != string.Empty)
                {
                    alternative = string.Format("{0} {1}", alternative, _workOrderEntityResult.Customer?.AltFirst);
                }
                else
                {
                    alternative = _workOrderEntityResult.Customer?.AltFirst;
                }
            }

            return alternative;
        }

        private void GetCurrentBtnStatus()
        {
            btnAddSubletEnabled = btnAddSublet.Enabled;
            btnAddTowEnabled = btnAddTow.Enabled;
            btnAddAdditionalConcernEnabled = btnAddAdditionalConcern.Enabled;
            btnConvertWOEnabled = btnConvertWO.Enabled;
            btnPartListEnabled = btnPartList.Enabled;
            btnReceiveOrderPartEnabled = btnReceiveOrderPart.Enabled;
            btnCarIsReadyTextEnabled = btnCarIsReadyText.Enabled;
            btnPleaseCallTextEnabled = btnPleaseCallText.Enabled;
            btnEstimateEnabled = btnEstimateText.Enabled;
            btnHistoryEnabled = btnHistory.Enabled;
            btnCloseWOEnabled = btnCloseWO.Enabled;
            btnPaymentsEnabled = btnPayments.Enabled;
            btnOrderPartsEnabled = btnOrderParts.Enabled;
            btnPricePartsEnabled = btnPriceParts.Enabled;
            btnAddPartsLaborEnabled = btnAddPartsLabor.Enabled;
            btnDeferEnabled = btnDefer.Enabled;
            btnUnDeferedEnabled = btnUnDefered.Enabled;
            btnSaveEnabled = btnSave.Enabled;
            btnAddCannedJobEnabled = btnAddCannedJob.Enabled;
        }
        private int GetEmployeeForJob()
        {
            var empIdForJob = FrmWorkOrderPartService.CreateService().GetUnitOfWork().workOrderRepository.GetEmployeeForJob(_workOrderId);
            if (empIdForJob == 0)
            {
                using (var frm = new FrmSelectTech())
                {
                    frm.ShowDialog();
                    if (frm.SelectedEmpId > 0)
                    {
                        empIdForJob = frm.SelectedEmpId;
                    }
                }
            }

            return empIdForJob;
        }

        private void InitControl()
        {
            _oneSourceAlert = new OneSourceAlert(components, this);
            _validationProvider = new ValidationProvider(toolTipMessage, this);
            RegisterEvents();
            InitCombobox(drdWorkOrderStatus, nameof(CustomData.Value), nameof(CustomData.Value));
            InitCombobox(drdEmployee, nameof(Employee.EmployeeId), nameof(Employee.FullName));
            InitCombobox(drdServiceWtr, nameof(Employee.EmployeeId), nameof(Employee.FullName));
            FormHelper.InitColumnsForTestNotification(rgvAppoinmentNotify, true);
        }
        private async Task Initialization()
        {
            try
            {
                wBSearching.Visible = true;
                wBSearching.StartWaiting();

                UseWaitCursor = true;
                Cursor.Current = Cursors.WaitCursor;
                await LoadFormData();
                LoadNotification();
            }
            finally
            {
                Cursor.Current = Cursors.Default;
            }

        }

    
        private bool IsRightAmount()
        {
            var labor = Convert.ToDecimal(lblLaborTotal.Text.Replace("$", ""));
            var sublet = Convert.ToDecimal(lblSublet.Text.Replace("$", ""));
            var towing = Convert.ToDecimal(lblTowing.Text.Replace("$", ""));
            var parts = Convert.ToDecimal(lblPartsTotal.Text.Replace("$", ""));
            var fei = Convert.ToDecimal(lblFEITaxTotal.Text.Replace("$", ""));
            var saleTax = Convert.ToDecimal(lblSalesTaxTotal.Text.Replace("$", ""));
            var shop = Convert.ToDecimal(lblShopCharge.Text.Replace("$", ""));
            var discount = Convert.ToDecimal(lblDiscountTotal.Text.Replace("$", ""));
            var creditCartFee = Convert.ToDecimal(lblCreditCard.Text.Replace("$", ""));
            var formula = labor + parts + sublet + towing + fei + saleTax + shop + creditCartFee - discount;
            var total = Convert.ToDecimal(lblTotal.Text.Replace("$", ""));

            if (Math.Truncate(formula) != Math.Truncate(total))
            {
                RadMessageBox.Show("Formula is wrong", "WARNING", MessageBoxButtons.OK, RadMessageIcon.Error);
                return false;
            }
            return true;
        }

        private async void LoadDataWorkOrder()
        {
            FrmWorkOrderPartService.CreateService().SPWorkOrderRepositoryUpdateIsUsingAsync(PersistentModels.CurrentWorkOrderId, true);
            _workOrderEntityResult = await FrmWorkOrderPartService.CreateService().GetByIdAsync(PersistentModels.CurrentWorkOrderId);
            if (_workOrderEntityResult != null)
            {
                if (_workOrderEntityResult.Vehicle != null && _workOrderEntityResult.Vehicle.VinInformation != null)
                {
                    _workOrderEntityResult.Vehicle.VinInformation.CustomerId = _workOrderEntityResult.CustomerId;
                }

                if (_workOrderEntityResult.VehicleId != null)
                {
                    vinInforHandler = new VinInforAutocompleteHandler(txtVinNumber, new RadControl[]
                        {
                            txtVinYear, txtVinMake, txtVinModel, txtTrim, txtVinBody, txtEngineType, txtVinDriveType,
                            txtVinTransmission, txtVinABS, txtTire
                        },
                        _validationProvider, null, wBSearching, null, _workOrderEntityResult.VehicleId.Value);
                }

                _customer = _workOrderEntityResult.Customer;
                vinInforHandler.SetCustomer(_workOrderEntityResult.Customer);
                List<VinInformation> vinInfors = FrmCommercialCustomerService.CreateService().GetVinInforByCustomerId(_workOrderEntityResult.CustomerId);
                vinInforHandler.SetVinInfo(vinInfors);

                vinInforHandler.SetVehicleIdAndCustomerId(_workOrderEntityResult.VehicleId, _workOrderEntityResult.CustomerId, _workOrderEntityResult.WorkOrderId);


                IsExemption = !string.IsNullOrEmpty(_workOrderEntityResult.Customer.ExemptionNo);
                workOrderType = _workOrderEntityResult.Type;
                btnCloseWO.Text = "CLOSE W.O";
                btnCloseWO.Enabled = false;
                btnAgreement.Visible = !PersistentModels.CurrentUser.UserGroup.Equals(UserGroupRole.TECHNICIAN.ToString(),
                        StringComparison.OrdinalIgnoreCase);

                if (string.IsNullOrEmpty(_workOrderEntityResult.AgreementFile))
                {
                    btnAgreement.Text = "RESEND AGREEMENT";
                }

                woToken = _workOrderEntityResult.Token;
                if (_workOrderEntityResult.Type == WorkOrderTypeEnum.WorkOrder.ToString())
                {
                    Text = "WORK ORDER";
                }
                else if (_workOrderEntityResult.Type == WorkOrderTypeEnum.Estimate.ToString())
                {
                    Text = "ESTIMATE";
                    lblWorkOrderTitle.Text = "Estimate #";
                    btnCloseWO.Text = "CLOSE ESTIMATE";
                }
                else if (_workOrderEntityResult.Type == WorkOrderTypeEnum.Appointment.ToString())
                {
                    Text = "APPOINTMENT";
                    lblWorkOrderTitle.Text = "Appointment #";
                }

                btnConvertWO.Visible = btnPrintInvoice.Visible =
                    (_workOrderEntityResult.Type == WorkOrderTypeEnum.Estimate.ToString()) ||
                    _workOrderEntityResult.Type == WorkOrderTypeEnum.Appointment.ToString();

                btnPayments.Visible = !btnPrintInvoice.Visible;


                if (PersistentModels.CurrentUser.UserGroup == UserGroupRole.ADMIN.ToString())
                {
                    btnOrderParts.Enabled =
                        btnReceiveOrderPart.Enabled = btnPayments.Enabled = true;

                    if (_workOrderEntityResult.Type == WorkOrderTypeEnum.WorkOrder.ToString()
                        || _workOrderEntityResult.Type == WorkOrderTypeEnum.Estimate.ToString())
                    {
                        btnCloseWO.Enabled = true;
                    }
                    //2023/06/21
                    //btnOrderParts.Enabled = !string.IsNullOrEmpty(_workOrderEntityResult.AgreementFile);
                }
                else
                {
                    var isReceivePartPermission =
                        PersistentModels.ListPermissionCurrentUserGroup.Any(p =>
                            p.ScreenCode == ScreenName.RECEIVE_ORDER_PART.ToString());
                    btnOrderParts.Enabled = btnReceiveOrderPart.Enabled =
                        btnPayments.Enabled = btnCloseWO.Enabled = isReceivePartPermission;

                    var isViewHistoryPermission =
                        PersistentModels.ListPermissionCurrentUserGroup.Any(p =>
                            p.ScreenCode == ScreenName.VEHICLE_HISTORY.ToString());
                    btnHistory.Enabled = isViewHistoryPermission;

                    btnOrderParts.Enabled = true;
                }

                await SetRestControlValue();
                SetContactBtn();

            }
        }

        void SetPermission()
        {
            tabPartLabor.Visible = tbMisc.Visible = tableTotal.Visible = false;
            if (PersistentModels.CurrentUser.UserGroup != UserGroupRole.ADMIN.ToString())
            {
                tabPartLabor.Visible = tbMisc.Visible = tableTotal.Visible = PersistentModels.ListPermissionCurrentUserGroup.Any(p =>
                       p.ScreenCode == ScreenName.DISPLAY_TOTAL.ToString());
            }
            else
            {
                tabPartLabor.Visible = tbMisc.Visible = tableTotal.Visible = true;
            }
        }

        private async Task LoadFormData()
        {
            _lstWorkOrderStatus = new List<CustomData>();
            _lstWorkOrderStatus.Add(new CustomData { Value = WorkOrderStatusEnum.NEEDS_TO_BE_PICKED_UP.GetEnumText() });
            _lstWorkOrderStatus.Add(new CustomData { Value = WorkOrderStatusEnum.UNASSIGNED.GetEnumText() });
            _lstWorkOrderStatus.Add(new CustomData { Value = WorkOrderStatusEnum.BEING_INSPECTED.GetEnumText() });
            _lstWorkOrderStatus.Add(new CustomData { Value = WorkOrderStatusEnum.TICKET_NEEDS_REVIEW.GetEnumText() });
            _lstWorkOrderStatus.Add(new CustomData { Value = WorkOrderStatusEnum.WAITING_AUTHORIZATION.GetEnumText() });
            _lstWorkOrderStatus.Add(new CustomData { Value = WorkOrderStatusEnum.PARTS_ON_ORDER.GetEnumText() });
            _lstWorkOrderStatus.Add(new CustomData { Value = WorkOrderStatusEnum.IN_PROGRESS.GetEnumText() });
            _lstWorkOrderStatus.Add(new CustomData { Value = WorkOrderStatusEnum.NEEDS_QC.GetEnumText() });
            _lstWorkOrderStatus.Add(new CustomData { Value = WorkOrderStatusEnum.FINISHED.GetEnumText() });
            _lstWorkOrderStatus.Add(new CustomData { Value = WorkOrderStatusEnum.NEEDS_TO_BE_DELIVERED.GetEnumText() });

            _lstWorkOrderStatus.Insert(0, new CustomData { CustomDataId = 0, Value = CustomDataMessage.PleaseChooseValue });

            List<Services.Model.Employee> listEmployee = await new OSCaching<List<Services.Model.Employee>>().GetOrCreateAsync(CacheKeyEnum.Employee.ToString(),
              async () => await Ultil.CachingEmployee());

            _lstTechnicians = await Ultil.GetTechList();

            _lstTechnicians.Insert(0, new Employee { EmployeeId = 0, FullName = CommonMessage.UNASSIGNED });

            var lstServiceWrt = await Ultil.GetServiceWrtList();

            lstServiceWrt.Insert(0, new Employee { EmployeeId = 0, FullName = CommonMessage.UNASSIGNED });

            if (PersistentModels.CurrentCompany != null)
            {
                var laborRates = FormUltil.GetLaborRate();
                _companyLaborRates[0] = new Tuple<decimal, string>(laborRates[0].Amount ?? 0M, laborRates[0].Name.EnsureReturnEmptyIfNullOrWhiteSpace());
                _companyLaborRates[1] = new Tuple<decimal, string>(laborRates[1].Amount ?? 0M, laborRates[1].Name.EnsureReturnEmptyIfNullOrWhiteSpace());
                _companyLaborRates[2] = new Tuple<decimal, string>(laborRates[2].Amount ?? 0M, laborRates[2].Name.EnsureReturnEmptyIfNullOrWhiteSpace());
                _totalSalesTaxCompany = (PersistentModels.CurrentCompany.Tax1 ?? 0M) + (PersistentModels.CurrentCompany.Tax2 ?? 0M) + (PersistentModels.CurrentCompany.Tax3 ?? 0M);
            }

            drdEmployee.DataSource = _lstTechnicians;
            drdWorkOrderStatus.DataSource = _lstWorkOrderStatus;
            drdServiceWtr.DataSource = lstServiceWrt;

            LoadDataWorkOrder();
            SetPermission();
        }


        private async void LoadNotification()
        {
            var shopNotificationResponse = await OSRest.GetRequest<ShopNotificationResponse>
                  ("https://osappdata.azurewebsites.net/notification/workorder?shop=" + PersistentModels.ShopName + "&workorder=" + PersistentModels.CurrentWorkOrderId);
            if (shopNotificationResponse != null)
            {
                rgvAppoinmentNotify.DataSource = shopNotificationResponse.ShopNotification;
            }
        }

        private WorkOrder PrepareWorkOrder(int vinYear, int miles)
        {
            return new WorkOrder()
            {
                Vehicle = new Vehicle
                {
                    VinInformation = new VinInformation
                    {
                        VehicleId = _vehicleId,
                        VinNumber = txtVinNumber.Text,
                        ModelYear = vinYear,
                        Make = txtVinMake.Text,
                        Model = txtVinModel.Text,
                        TrimLevel = txtTrim.Text,
                        EngineType = txtEngineType.Text,
                        TransmissionShort = txtVinTransmission.Text,
                        Driveline = txtVinDriveType.Text,
                        Tires = txtTire.Text,
                        AntiBrakeSystem = txtVinABS.Text,
                        BodyStyle = txtVinBody.Text,
                        Tag = txtVinTag.Text
                    },
                    CustomerId = _customerId
                },
                WorkOrderId = PersistentModels.CurrentWorkOrderId,
                Miles = miles,
                EmployeeId = (int?)drdEmployee.SelectedValue ?? _workOrderEntityResult.EmployeeId,
                ServiceWriterId = (int?)drdServiceWtr.SelectedValue ?? _workOrderEntityResult.ServiceWriterId,
                Type = _workOrderEntityResult.Type,
                Path = _workOrderEntityResult.Type,
                WorkOrderStatusName = drdWorkOrderStatus.Text,
                Note = txtNotes.Text
            };
        }

        private async Task ProcessContent(Notification notification)
        {
            var reviewContent = OSHelper.NotificationContent(notification, PersistentModels.CurrentCompany.ReviewContent);
            if (string.IsNullOrWhiteSpace(_customer.Email))
            {
                FrmSendMail frmSendMail = new FrmSendMail(_customer, reviewContent);
                frmSendMail.ShowDialog();
            }
            else
            {
                wBSearching.StartWaiting();
                wBSearching.Visible = true;

                var resp = await OSRest.SendMail(_customer.Email, reviewContent, "Review Reminder");

                wBSearching.StopWaiting();
                wBSearching.Visible = false;
            }
        }

        private void ProcessErrorOccur(Repositories.ResultModel<WorkOrder> result)
        {
            switch (result.ErrorCode)
            {
                case ErrorCode.VinNumberRequired:
                    _validationProvider.SetError(txtVinNumber, result.Message);
                    break;

                case ErrorCode.VinInformationRequired:
                case ErrorCode.VinInformationAlreadyExist:
                    _validationProvider.SetError(txtVinYear, result.Message);
                    break;

                case ErrorCode.VinNumberAlreadyExist:
                case ErrorCode.InvalidVinNumber:
                    _validationProvider.SetError(txtVinNumber, result.Message);
                    break;

                case ErrorCode.VehicleAreadyExist:
                    _oneSourceAlert.Show(result.Message);
                    break;

                case ErrorCode.YearRequired:
                    _validationProvider.SetError(txtVinYear, result.Message);
                    break;

                case ErrorCode.ModelRequired:
                    _validationProvider.SetError(txtVinModel, result.Message);
                    break;

                case ErrorCode.MakeRequired:
                    _validationProvider.SetError(txtVinMake, result.Message);
                    break;

                case ErrorCode.WoStatusRequired:
                    _validationProvider.SetError(drdWorkOrderStatus, result.Message);
                    break;

                case ErrorCode.InvalidConcern:
                    throw new OneSourceException(result.Message);
                default:
                    throw new OneSourceException(result.Message);
            }
        }

        private void ProcessLstConcerns()
        {
            var lstUndeferedConcerns = _concernPanelControl.LstConcernControls.Where(x => x.Concern != null && !x.Concern.IsDefer).ToList();
            foreach (var item in lstUndeferedConcerns)
            {
                foreach (var lt in item.Concern?.ListLaborGuides)
                {
                    switch (lt.Type)
                    {
                        case "SUBLET":
                            TotalSublet += lt.LaborTimeAmount;
                            break;

                        case "TOWING":
                            TotalTowing += lt.LaborTimeAmount;
                            break;

                        default:
                            TotalLabors += lt.LaborTimeAmount;
                            break;
                    }

                    foreach (var pp in lt.PickedParts)
                    {
                        if (pp.Total.HasValue)
                        {
                            TotalParts += pp.Total.Value;
                        }
                    }
                }
            }
        }

        private async Task ProcessNextStep()
        {
            _concernPanelControl.ConcernValueChange += ConcernPanelValueChanged;
            _concernPanelControl.DeferUndeferConcernClick += DeferUndeferConcern;
            _concernPanelControl.DeferUndeferLaborGuideClick += DeferUndeferLaborGuide;
            _concernPanelControl.AddJobClick += _concernPanelControl_AddJobClick;
            _concernPanelControl.EditJobClick += _concernPanelControl_EditJobClick;
            _concernPanelControl._reloadConcern = ReloadConcerns;

            panelGrid.Controls.Clear();
            _concernPanelControl.Visible = false;
            panelGrid.Controls.Add(_concernPanelControl);
            lblCreditCardConv.Text = _workOrderEntityResult.CreditCardCharge.ToString();
            CalculateTotal();

            if (_workOrderEntityResult.Customer.IsCommercialCustomer && _workOrderEntityResult.Customer.AccountTermId != null)
            {
                var accountTerm = FrmWorkOrderPartService.CreateService().SPCustomDataRepositoryGetOneByIdAsync(_workOrderEntityResult.Customer.AccountTermId);
                if (accountTerm != null && accountTerm.Value == "Net 10th")
                {
                    IsAvailabeCloseWorkOrder = true;
                }
            }

            var existReceivePart = await FrmWorkOrderPartService.CreateService().SPReceiveOrderPartRepositoryIsExistOrderedPartAsync(PersistentModels.CurrentWorkOrderId);
            btnReceiveOrderPart.Enabled = existReceivePart;

            var returnParts = await FrmWorkOrderPartService.CreateService().SPReceiveOrderPartRepositoryPartForReturnCountAsync(PersistentModels.CurrentWorkOrderId);
            if (returnParts > 0)
            {
                btnReturnPart.Enabled = true;
            }
            else
            {
                btnReturnPart.Enabled = false;
            }

            _concernPanelControl.Visible = true;

            GetCurrentBtnStatus();
            UpdateBtnStatus();
            drdWorkOrderStatus.SelectedValueChanged += DrdWorkOrderStatus_SelectedValueChanged;

            if (!tbCustomInfor.Visible)
            {
                tableTop.ColumnStyles[0] = new ColumnStyle(SizeType.Percent, 0);
            }
        }

        private async Task ProcessNullOrEmptyAgreementFile()
        {
            wBSearching.StartWaiting();
            wBSearching.Visible = true;
            _workOrderEntityResult.Customer.Email = _customer.Email;
            _workOrderEntityResult.Customer.Phone1 = _customer.Phone1;

            await OSHelper.ResendAgreementFile(_workOrderEntityResult, (mailContent, token) =>
            {
                wBSearching.Visible = false;
                wBSearching.StopWaiting();
                using (FrmSendEmail frm = new FrmSendEmail(new string[] { _workOrderEntityResult.Customer.Email }))
                {
                    frm.ShowDialog();
                    if (frm.IsSend)
                    {
                        wBSearching.Visible = false;
                        wBSearching.Text = "Agreement is being sent ...";
                        OSRest.SendMultiMail(frm.MailList, mailContent, "NEW WORK ORDER");
                        RadMessageBox.Show("AGREEMENT IS SENT", "Agreement", MessageBoxButtons.OK, RadMessageIcon.Info);
                    }
                    wBSearching.StopWaiting();
                    wBSearching.Visible = false;
                    wBSearching.Text = "Loading , please wait ...";
                    FrmWorkOrderPartService.CreateService().SPWorkOrderRepositoryUpdateTokenAsync(_workOrderEntityResult.WorkOrderId, token);
                }

            });

        }
        private void ProcessResultError(Repositories.ResultModel<WorkOrder> result)
        {
            switch (result.ErrorCode)
            {
                case ErrorCode.VinNumberRequired:
                    _validationProvider.SetError(txtVinNumber, result.Message);
                    break;

                case ErrorCode.VinInformationRequired:
                case ErrorCode.VinInformationAlreadyExist:
                    _validationProvider.SetError(txtVinYear, result.Message);
                    break;

                case ErrorCode.VehicleAreadyExist:
                    _oneSourceAlert.Show(result.Message);
                    break;

                case ErrorCode.InvalidConcern:
                    throw new OneSourceException(result.Message);
                default:
                    throw new OneSourceException(result.Message);
            }
        }

        private void ProcessResultOK(List<int> lstSelectedConcerns)
        {
            AmountDiscount = 0;
            if (lstSelectedConcerns.Count() > 0)
            {
                var concern = FrmWorkOrderPartService.CreateService().SPConcernRepositoryGetOneByIdAsync(lstSelectedConcerns.First());
                OSRest.Defer(new
                {
                    shop = PersistentModels.ShopName,
                    email = _currentCustomer.Email,
                    workOrderid = PersistentModels.CurrentWorkOrderId,
                    concernId = lstSelectedConcerns.First(),
                    reminderDay = "",
                    content = PersistentModels.CurrentCompany.ReviewContent,
                    concernName = concern.ConcernValue
                });
            }

            ReloadConcerns();
        }

        private void RegisterEvents()
        {
            btnPriceParts.Click += BtnPriceParts_Click;
            btnPartList.Click += btnPartList_Click;
            btnConvertWO.Click += BtnConvertWO_Click;
            btnSave.Click += btnSave_Click;
            btnUnDefered.Click += btnUnDefered_Click;
            btnDefer.Click += btnDefer_Click;
            btnAddPartsLabor.Click += BtnAddPartsLabor_Click;
            btnOrderParts.Click += btnOrderParts_Click;
            btnReceiveOrderPart.Click += btnReceiveOrderPart_Click;
            btnPayments.Click += btnPayments_Click;
            btnCloseWO.Click += btnCloseWO_Click;
            btnHistory.Click += btnHistory_Click;
            btnCancel.Click += btnCancel_Click;
            txtVinYear.KeyPress += txtVinYear_KeyPress;
            txtVinTag.TextChanging += txtVinTag_TextChanging;
            btnAddAdditionalConcern.Click += BtnAddAdditionalConcern_Click;
            btnAddSublet.Click += BtnAddSublet_Click;
            btnAddCannedJob.Click += BtnAddCannedJob_Click;
            btnAddTow.Click += BtnAddTow_Click;
            Shown += FrmWorkOrderPart_Shown;
            var ctx = new ContextMenu();
            var delete = new MenuItem("Delete this ticket", Delete_Click);
            ctx.MenuItems.Add(delete);
            lblWorkOrderTitle.ContextMenu = ctx;
            FormClosed += FrmWorkOrder_FormClosed;
            btnPrint.Click += BtnPrint_Click;
            btnEstimateEmail.Click += BtnEstimateEmail_Click;
            btnAgreement.Click += BtnAgreement_Click;
            btnReturnPart.Click += new EventHandler(btnReturnPart_Click);
            btnViewInvoice.Click += btnViewInvoice_Click;
            btnCarIsReadyEmail.Click += new EventHandler(btnCarIsReadyEmail_Click);
            btnCarIsReadyText.Click += new EventHandler(btnCarIsReadyText_Click);
            btnPleaseCallEmail.Click += new EventHandler(btnPleaseCallEmail_Click);
            btnPleaseCallText.Click += new EventHandler(btnPleaseCallText_Click);
            btnEstimateText.Click += new EventHandler(btnEstimateText_Click);
            //btnShareInfor.Click += new EventHandler(btnShareInfor_Click);
            btnTowingDocument.Click += new EventHandler(btnTowingDocument_Click);
        }

        private void SetControlValue()
        {
            _vehicleId = _workOrderEntityResult.VehicleId.Value;
            txtVinYear.Text = _workOrderEntityResult.ModelYear?.ToString();
            txtVinNumber.Text = _workOrderEntityResult.Vehicle?.VinInformation?.VinNumber.Trim();
            txtVinMake.Text = _workOrderEntityResult.Make;
            txtVinModel.Text = _workOrderEntityResult.Model;

            txtVinMiles.Value = _workOrderEntityResult.Miles;
            txtVinTag.Text = _workOrderEntityResult.Vehicle?.VinInformation?.Tag;
            txtTrim.Text = _workOrderEntityResult.Vehicle?.VinInformation?.TrimLevel;
            txtEngineType.Text = _workOrderEntityResult.Vehicle?.VinInformation?.EngineType;
            txtVinTransmission.Text = _workOrderEntityResult.Vehicle?.VinInformation?.TransmissionShort;
            txtVinDriveType.Text = _workOrderEntityResult.Vehicle?.VinInformation?.Driveline;
            txtTire.Text = _workOrderEntityResult.Vehicle?.VinInformation?.Tires;
            txtVinABS.Text = _workOrderEntityResult.Vehicle?.VinInformation?.AntiBrakeSystem;
            txtVinBody.Text = _workOrderEntityResult.Vehicle?.VinInformation?.BodyStyle;
            txtCoolant.Text = _workOrderEntityResult.Vehicle?.VinInformation?.CoolantCapacity.ToString();
            txtRefrigerant.Text = _workOrderEntityResult.Vehicle?.VinInformation?.RefrigerantCapacity.ToString();

            lblCoolantType.Text = _workOrderEntityResult.Vehicle?.VinInformation?.CoolantType;
            lblAutomaticTransmissionCapacity.Text = _workOrderEntityResult.Vehicle?.VinInformation?.AutomaticTransmissionCapacity.ToString();
            lblAcOilCapacity.Text = _workOrderEntityResult.Vehicle?.VinInformation?.ACOilCapacity?.ToString();

            //2023/06/06
            txtNotes.Text = _workOrderEntityResult.Note;
        }

        private async Task SetRestControlValue()
        {
            string alternative = GetAlternative();

            lblCustomerAlternative.Text = alternative;
            lblAcceptOldPart.Visible = _workOrderEntityResult.Customer.AcceptOldPart;

            _currentVehicle = _workOrderEntityResult.Vehicle?.VinInformation;
            SetControlValue();
            _oldVehicle = new
            {
                vinNumber = txtVinNumber.Text,
                make = txtVinMake.Text,
                model = txtVinModel.Text,
                year = Convert.ToInt16(string.IsNullOrEmpty(txtVinYear.Text) ? "0" : txtVinYear.Text)
            };

            if (txtVinYear.Text != "0" && !string.IsNullOrEmpty(txtVinYear.Text))
            {
            }
            else
            {
                txtVinYear.Text = "";
            }

            _customerId = _workOrderEntityResult.Customer.CustomerId;
            lblRefereed.Text = _workOrderEntityResult.ReferredBy;
            if (_workOrderEntityResult.WorkOrderStatusName != WorkOrderStatusEnum.CLOSED.ToString())
            {
                if (_workOrderEntityResult.EmployeeId == null)
                    drdEmployee.SelectedIndex = 0;
                else
                {
                    drdEmployee.SelectedValue = _workOrderEntityResult.EmployeeId;
                }

                // Employee quit
                if (_workOrderEntityResult.EmployeeId != null && _workOrderEntityResult.EmployeeId != 0 && drdEmployee.SelectedValue == null)
                {
                    drdEmployee.Text = _workOrderEntityResult.EmployeeName;
                }
            }
            else
            {
                drdEmployee.Visible = false;
                btnSave.Visible = false;

                txtTech.Text = _workOrderEntityResult.EmployeeName;
                txtTech.Visible = true;
                btnClose.Visible = true;
            }

            if (_workOrderEntityResult.ServiceWriterId == null)
                drdServiceWtr.SelectedIndex = 0;
            else
            {
                drdServiceWtr.SelectedValue = _workOrderEntityResult.ServiceWriterId;
            }

            // Employee quit
            if (_workOrderEntityResult.ServiceWriterId != null && _workOrderEntityResult.ServiceWriterId != 0 && drdServiceWtr.SelectedValue == null)
            {
                var emp = FrmWorkOrderPartService.CreateService().GetUnitOfWork().EmployeeRepository.GetOneById(_workOrderEntityResult.ServiceWriterId);
                drdServiceWtr.Text = emp.FirstName + " " + emp.LastName;
            }
            else if (drdServiceWtr.Items.Count() == 2)
            {
                drdServiceWtr.SelectedIndex = 1;
            }

            if (_workOrderEntityResult.WorkOrderStatusName == WorkOrderStatusEnum.CLOSED.ToString())
            {
                drdWorkOrderStatus.Text = WorkOrderStatusEnum.CLOSED.ToString();
                drdWorkOrderStatus.ReadOnly = true;
            }
            else
            {
                drdWorkOrderStatus.SelectedValue = _workOrderEntityResult.WorkOrderStatusName;
            }

            btnDefer.Enabled = false;
            btnUnDefered.Enabled = false;
            _concernPanelControl = new ConcernPanelControl(_workOrderEntityResult.Concerns,
                                                                    _companyLaborRates,
                                                                    _workOrderEntityResult.WorkOrderStatusName == WorkOrderStatusEnum.FINISHED.GetEnumText(), _workOrderEntityResult,
                                                                    PersistentModels.CurrentCompany, this, _currentVehicle, ReloadConcerns, CloseFrm, _customerId
                                                                    , SumTotal);
            await ProcessNextStep();

            wBSearching.Visible = false;
            wBSearching.StopWaiting();
            UseWaitCursor = false;
        }
        private void txtVinTag_TextChanging(object sender, TextChangingEventArgs e)
        {
            bool isValid = StringHelper.IsValidTag(e.NewValue);
            if (!isValid)
            {
                e.Cancel = true;
            }
        }
        private void txtVinYear_KeyPress(object sender, KeyPressEventArgs e)
        {
            string input = e.KeyChar.ToString();
            if (string.IsNullOrWhiteSpace(input) || e.KeyChar == (char)8 || e.KeyChar == (char)13) return;

            bool isValid = StringHelper.IsValidIngeter(input);
            if (!isValid)
            {
                e.Handled = true;
            }
        }
        private async void UnDeferConcern(List<Entities.Concern> lstSelectedUnDeferedConcerns, List<UnDeferedLaborGuideParameter> lstSelectedUndeferedLaborGuides)
        {
            if (lstSelectedUnDeferedConcerns.Count > 0 || lstSelectedUndeferedLaborGuides.Count > 0)
            {
                var result = await FrmWorkOrderPartService.CreateService().SPWorkOrderRepositoryUnDeferedConcernsAsync(new UnDeferedConcernParameter
                {
                    WorkOrderId = PersistentModels.CurrentWorkOrderId,
                    ListUnDeferedConcerns = lstSelectedUnDeferedConcerns,
                    ListUnDeferedLaborGuides = lstSelectedUndeferedLaborGuides
                });
                _validationProvider.IsShowTooltip = false;
                _validationProvider.IsFocus = false;
                ResetValidation();
                if (result.IsError)
                {
                    throw new OneSourceException(result.Message);
                }

                ReloadConcerns();
            }
        }
        private void UpdateBtnStatus()
        {
            if (_workOrderEntityResult.WorkOrderStatusName == WorkOrderStatusEnum.CLOSED.ToString())
            {
                btnAddSublet.Enabled =
                btnAddTow.Enabled =
                btnAddAdditionalConcern.Enabled =
                btnConvertWO.Enabled =
                btnPartList.Enabled =
                btnReceiveOrderPart.Enabled =
                btnCarIsReadyText.Enabled =
                btnCarIsReadyEmail.Enabled =
                btnPleaseCallText.Enabled =
                btnPleaseCallEmail.Enabled =
                 btnEstimateText.Enabled =
                btnEstimateEmail.Enabled =
                btnHistory.Enabled =
                btnCloseWO.Enabled =
                btnOrderParts.Visible =
                btnPriceParts.Enabled =
                btnAddPartsLabor.Enabled =
                btnDefer.Enabled =
                btnUnDefered.Enabled =
                btnSave.Enabled =
                btnAddCannedJob.Enabled = false;
            }
            else
            {
                btnAddSublet.Enabled = btnAddSubletEnabled;
                btnAddTow.Enabled = btnAddTowEnabled;
                btnAddAdditionalConcern.Enabled = btnAddAdditionalConcernEnabled;
                btnConvertWO.Enabled = btnConvertWOEnabled;
                btnPartList.Enabled = btnPartListEnabled;
                btnReceiveOrderPart.Enabled = btnReceiveOrderPartEnabled;
                btnCarIsReadyText.Enabled = btnCarIsReadyEmail.Enabled = btnCarIsReadyTextEnabled;
                btnPleaseCallText.Enabled = btnPleaseCallEmail.Enabled = btnPleaseCallTextEnabled;
                btnEstimateText.Enabled = btnEstimateEmail.Enabled = btnEstimateEnabled;
                btnHistory.Enabled = btnHistoryEnabled;
                btnCloseWO.Enabled = btnCloseWOEnabled;
                btnAddCannedJob.Enabled = btnAddCannedJobEnabled;
                btnPayments.Enabled = btnPaymentsEnabled;
                btnOrderParts.Visible = true;
                btnPriceParts.Enabled = btnPricePartsEnabled;
                btnAddPartsLabor.Enabled = btnAddPartsLaborEnabled;
                btnDefer.Enabled = btnDeferEnabled;
                btnUnDefered.Enabled = btnUnDeferedEnabled;
                btnSave.Enabled = btnSaveEnabled;

                SetContactBtn();
            }

        }

        private void SetContactBtn()
        {
            bool isEstimateText, isEstimateEmail, isEstimatePhone, isPleaseCallText, isPleaseCallEmail, isPleaseCallPhone, isCarIsReadyText, isCarIsReadyEmail, isCarIsReadyPhone;

            OSHelper.ConverStringToBool(_customer.Estimate, out isEstimateText, out isEstimateEmail, out isEstimatePhone);
            OSHelper.ConverStringToBool(_customer.PleaseCall, out isPleaseCallText, out isPleaseCallEmail, out isPleaseCallPhone);
            OSHelper.ConverStringToBool(_customer.CarIsReady, out isCarIsReadyText, out isCarIsReadyEmail, out isCarIsReadyPhone);

            btnEstimateText.Enabled = btnEstimateText.Enabled ? isEstimateText : btnEstimateText.Enabled;
            btnEstimateEmail.Enabled = btnEstimateEmail.Enabled ? isEstimateEmail : btnEstimateEmail.Enabled;

            btnPleaseCallText.Enabled = btnPleaseCallText.Enabled ? isPleaseCallText : btnPleaseCallText.Enabled;
            btnPleaseCallEmail.Enabled = btnPleaseCallEmail.Enabled ? isPleaseCallEmail : btnPleaseCallEmail.Enabled;

            btnCarIsReadyText.Enabled = btnCarIsReadyText.Enabled ? isCarIsReadyText : btnCarIsReadyText.Enabled;
            btnCarIsReadyEmail.Enabled = btnCarIsReadyEmail.Enabled ? isCarIsReadyEmail : btnCarIsReadyEmail.Enabled;
        }
        private async Task<string> UploadReceipt()
        {
            var totalAmount = Convert.ToDecimal(lblTotal.Text.Replace("$", ""));
            var report = new Receipt();
            await report.LoadData(PersistentModels.CurrentWorkOrderId, PersistentModels.CurrentCompany);

            string fileName = string.Format("{0}/{1}/{2}_{3}.pdf", PersistentModels.ShopName, "RECEIPT", _currentCustomer.FirstName.Replace("'", "") + _currentCustomer.LastName.Replace("'", "") + _workOrderNumber, DateTime.Now.ToString("ddMMyyyy"));

            ReportProcessor reportProcessor = new ReportProcessor();
            Telerik.Reporting.InstanceReportSource instanceReportSource = new Telerik.Reporting.InstanceReportSource();
            instanceReportSource.ReportDocument = report;
            RenderingResult result = reportProcessor.RenderReport("PDF", instanceReportSource, null);

            var res = await OSRest.UploadFile(new UploadReq
            {
                FileName = fileName,
                Content = result.DocumentBytes
            });

            return res.ErrMsg == "" ? res.FileUrl : string.Empty;
        }

        async void EstimateIsReadyText()
        {
            var requestData = new
            {
                email = _currentCustomer.Email,
                vehicle = txtVinYear.Text + " " + txtVinMake.Text + " " + txtVinModel.Text,
                servicingShop = PersistentModels.CurrentShop,
                servicingWorkOrderId = _workOrderId.ToString(),
                vehicleStatus = drdWorkOrderStatus.Text
            };
            var resp = await OSRest.PostRequest<string>(requestData, "https://a1ssite.azurewebsites.net/workorder/estimateisready");
        }
        private void _concernPanelControl_AddJobClick(object sender, int concernId)
        {
            using (var frm = new FrmSelectLaborguide(_currentVehicle, concernId))
            {
                frm.ShowDialog();
                if (frm.IsChanged)
                {
                    ReloadConcerns();
                }
            }
        }
        private void _concernPanelControl_EditJobClick(object sender, Entities.Concern currentConcern)
        {
            using (var frmAdditionalConcern = new FrmAddAdditionalConcern(PersistentModels.CurrentVinInfo, currentConcern))
            {
                frmAdditionalConcern.ShowDialog();
                if (frmAdditionalConcern.IsSaved)
                {
                    ReloadConcerns();
                }
            }
        }
        private void BtnAddAdditionalConcern_Click(object sender, EventArgs e)
        {
            Entities.Concern concern = null;
            if (drdEmployee.SelectedValue?.ToString() != "0")
            {
                concern = new Entities.Concern();
                concern.EmployeeId = Convert.ToInt32(drdEmployee.SelectedValue);
            }
            using (var frmAdditionalConcern = new FrmAddAdditionalConcern(_currentVehicle, concern, true))
            {
                frmAdditionalConcern.ShowDialog();
                if (frmAdditionalConcern.IsSaved)
                {
                    ReloadConcerns();
                }
            }
        }
        private void BtnAddCannedJob_Click(object sender, EventArgs e)
        {
            var frmCannedJobListing = new FrmCannedJobListing(_workOrderId, 0, string.Empty);
            frmCannedJobListing.PickCannedJob += FrmCannedJobListing_PickCannedJob;
            frmCannedJobListing.ShowDialog();
        }
        private async void BtnAddPartsLabor_Click(object sender, EventArgs e)
        {
            int vinYear;
            int.TryParse(txtVinYear.Text, out vinYear);
            var vehicle = new Vehicle
            {
                VinInformation = new VinInformation
                {
                    VehicleId = _vehicleId,
                    VinNumber = txtVinNumber.Text,
                    ModelYear = vinYear,
                    Make = txtVinMake.Text,
                    Model = txtVinModel.Text,
                    TrimLevel = txtTrim.Text,
                    EngineType = txtEngineType.Text,
                    TransmissionShort = txtVinTransmission.Text,
                    Driveline = txtVinDriveType.Text,
                    Tires = txtTire.Text,
                    AntiBrakeSystem = txtVinABS.Text,
                    BodyStyle = txtVinBody.Text,
                    Tag = txtVinTag.Text
                },
                CustomerId = _customerId
            };

            await FrmWorkOrderPartService.CreateService().SPWorkOrderRepositorySaveVehicleForCustomerAsync(vehicle, null);
            var frm = new FrmLaborGuide(vehicle.VinInformation, this);
            frm.ShowDialog(this);
        }
        private void BtnAddSublet_Click(object sender, EventArgs e)
        {
            using (var frm = new FrmAddSublet())
            {
                frm.ShowDialog();
                if (frm.IsSaved)
                {
                    ReloadConcerns();
                    btnReceiveOrderPart.Enabled = true;
                }
            }
        }
        private void BtnAddTow_Click(object sender, EventArgs e)
        {
            if (workOrderType != WorkOrderTypeEnum.WorkOrder.ToString())
            {
                RadMessageBox.Show("YOU NEED TO CONVERT TO WORK ORDER BEFORE ADDING A NEW TOW", "WORKORDER ONLY", MessageBoxButtons.OK, RadMessageIcon.Error);
                return;
            }
            using (var frm = new FrmAddTowing(_workOrderEntityResult, (val) =>
            {
                if (val.Contains("drop", StringComparison.OrdinalIgnoreCase))
                {
                    drdWorkOrderStatus.SelectedValue = WorkOrderStatusEnum.NEEDS_TO_BE_DELIVERED.GetEnumText();
                }
                else
                {
                    drdWorkOrderStatus.SelectedValue = WorkOrderStatusEnum.NEEDS_TO_BE_PICKED_UP.GetEnumText();
                }
            }))
            {
                frm.ShowDialog();
                if (frm.IsSaved)
                {
                    ReloadConcerns();
                    btnReceiveOrderPart.Enabled = true;
                }
            }
        }
        private async void BtnAgreement_Click(object sender, EventArgs e)
        {
            if (string.IsNullOrEmpty(_workOrderEntityResult.AgreementFile))
            {
                await ProcessNullOrEmptyAgreementFile();
            }
            else
            {
                var lstAgreementFiles = new List<string>() { _workOrderEntityResult.AgreementFile };
                using (var frm = new FrmViewSlip(lstAgreementFiles))
                {
                    frm.ShowDialog();
                }
            }
        }
        private void btnCancel_Click(object sender, EventArgs e)
        {
            Close();
        }
        private void btnCarIsReadyEmail_Click(object sender, EventArgs e)
        {
            if (!string.IsNullOrEmpty(PersistentModels.CurrentCompany.CarIsReadyContent))
            {
                if (string.IsNullOrEmpty(_currentCustomer.Email))
                {
                    RadMessageBox.Show("NO EMAIL TO SEND", "Car Is Ready", MessageBoxButtons.OK, RadMessageIcon.Error);
                    return;
                }
                wBSearching.Visible = true;
                wBSearching.StartWaiting();

                var notification = new Notification
                {
                    FirstName = _customer.FirstName.ToLower(),
                    LastName = _customer.LastName.ToLower(),
                    Year = txtVinYear.Text,
                    Make = txtVinMake.Text,
                    Model = txtVinModel.Text,
                };

                var body = OSHelper.NotificationContent(notification, PersistentModels.CurrentCompany.CarIsReadyContent);
                string linkNewWorkOrder = $"<br>Click on the link, login with your account to pay online: <a href='https://www.auto1source.com/payment?customer= + {_currentCustomer.Email}'>Click here to pay</a>";
                wBSearching.Visible = false;
                using (FrmSendEmail frm = new FrmSendEmail(new string[] { _workOrderEntityResult.Customer.Email }))
                {
                    frm.ShowDialog();
                    if (frm.IsSend)
                    {
                        wBSearching.Visible = true;
                        wBSearching.Text = "Email is being sent ...";
                        OSRest.SendMultiMail(frm.MailList, body + linkNewWorkOrder, "CAR IS READY");
                        RadMessageBox.Show("EMAIL IS SENT", "Email", MessageBoxButtons.OK, RadMessageIcon.Info);
                    }
                }

                wBSearching.Text = "Loading , please wait ...";
                wBSearching.Visible = false;
                wBSearching.StopWaiting();
            }
            else
            {
                RadMessageBox.Show("NO CARISREADY CONTENT. PLEASE SET UP IN MARKETING", "Email", MessageBoxButtons.OK, RadMessageIcon.Error);
            }
        }

        private async void btnCarIsReadyText_Click(object sender, EventArgs e)
        {
            if (string.IsNullOrEmpty(PersistentModels.CurrentCompany.CarIsReadyContent))
            {
                RadMessageBox.Show("NO CAR IS READY CONTENT. PLEASE SET UP IN MARKETING", "Car Is Ready", MessageBoxButtons.OK, RadMessageIcon.Error);
                return;
            }

            if (string.IsNullOrEmpty(_currentCustomer.Phone1))
            {
                RadMessageBox.Show("NO PHONE NUMBER TO SEND", "Car Is Ready", MessageBoxButtons.OK, RadMessageIcon.Error);
                return;
            }

            wBSearching.Visible = true;
            wBSearching.StartWaiting();
            wBSearching.Text = "Loading , please wait ...";

            var notification = new Notification
            {
                FirstName = _customer.FirstName.ToLower(),
                LastName = _customer.LastName.ToLower(),
                Year = txtVinYear.Text,
                Make = txtVinMake.Text,
                Model = txtVinModel.Text,
            };

            var body = OSHelper.NotificationContent(notification, PersistentModels.CurrentCompany.CarIsReadyContent, true) + $"\nClick on the link and login to pay online: https://www.auto1source.com/payment?customer={_currentCustomer.Email}";
            wBSearching.Visible = false;

            string status = "";
            using (FrmSendSMS frm = new FrmSendSMS(new string[] { _currentCustomer.Phone1 }))
            {
                frm.ShowDialog();
                if (frm.isSend)
                {
                    wBSearching.Visible = true;
                    wBSearching.Text = "SMS is being sent...";
                    status = await OSNotification.NotifyMultySMS(frm.PhoneLst, body);
                    if (status == "")
                    {
                        RadMessageBox.Show("SMS is sent", "Car Is Ready", MessageBoxButtons.OK, RadMessageIcon.Info);
                    }
                    else
                    {
                        RadMessageBox.Show(status, "Car Is Ready", MessageBoxButtons.OK, RadMessageIcon.Error);
                    }
                }
            }
            wBSearching.Visible = false;
            wBSearching.StopWaiting();
            wBSearching.Text = "Loading , please wait ...";
        }
        private void btnClose_Click(object sender, EventArgs e)
        {
            Close();
        }
        private async void btnCloseWO_Click(object sender, EventArgs e)
        {
            if (workOrderType == WorkOrderTypeEnum.Estimate.ToString())
            {
                //https://app.asana.com/0/1176431845863857/1205360069455737
                bool isValid = await SaveWO();
                if (!isValid)
                {
                    return;
                }

                var lstSelectedConcerns = _concernPanelControl.LstConcernControls.Where(x => !x.Concern.IsDefer).Select(x => x.Concern.ConcernId).ToList();
                var lstSelectedLaborGuides = _concernPanelControl.LstConcernControls.SelectMany(x => x.ListLaborGuideTitle).Where(x => x.IsSelected && !x.IsDefered).Select(x => new DeferedLaborGuideParameter
                {
                    LaborTimeId = x.LaborTimeId,
                    ConcernId = x.Concern.ConcernId
                }).ToList();
                DeferConcern(lstSelectedConcerns, lstSelectedLaborGuides);

                var workOrderStatusClose =
                await FrmWorkOrderPartService.CreateService().SPCustomDataRepositoryGetByTypeAndValueAsync(CustomDataTypeEnum.WORK_ORDER_STATUS,
                    WorkOrderStatusEnum.CLOSED.ToString());

                if (workOrderStatusClose.Count == 0)
                    throw new OneSourceException("Work Order Status 'CLOSED' could not be found!");

                _workOrderEntityResult.WorkOrderStatusName = workOrderStatusClose.ElementAt(0).Value;
                _workOrderEntityResult.ClosedTime = DateTime.Now;

                FrmWorkOrderPartService.CreateService().SPWorkOrderRepositoryUpdateAsync(_workOrderEntityResult, "WorkOrderId");

                Close();
            }
            else
            {
                var totalDue = lblTotalDue.Text.Replace("$", "").Trim();
                if (!IsAvailabeCloseWorkOrder && Convert.ToDecimal(totalDue) > 0)
                {
                    //https://app.asana.com/0/1176431845863857/1202796230519593
                    var lstCommercialAccountTerms = await FrmCommercialCustomerService.CreateService().GetByTypeAndValue(CustomDataTypeEnum.ACCOUNT_TERMS);
                    var net10th = lstCommercialAccountTerms.Where(x => x.Value == DepositTypeEnum.NET_10TH.GetEnumText()).ToList();
                    if (net10th.Any() && net10th.First().CustomDataId == _workOrderEntityResult.Customer.AccountTermId)
                    {
                    }
                    else
                    {
                        RadMessageBox.Show("YOU STILL HAVE BALANCE DUE", "WARNING", MessageBoxButtons.OK, RadMessageIcon.Error);
                        return;
                    }

                }

                //https://app.asana.com/0/1176431845863857/1205360069455737
                bool isValid = await SaveWO();
                if (isValid)
                {
                    CloseWorkOrder();
                }
            }
        }
        private async void BtnConvertWO_Click(object sender, EventArgs e)
        {
            wBSearching.Visible = true;
            wBSearching.StartWaiting();

            WorkOrder currentWo = FrmWorkOrderPartService.CreateService().SPWorkOrderRepositoryGetOneAsync(new WorkOrder()
            {
                WorkOrderId = PersistentModels.CurrentWorkOrderId
            }, "WorkOrderId");

            //2023/02/15
            await ProcessNullOrEmptyAgreementFile();

            var result = await FrmWorkOrderPartService.CreateService().SPWorkOrderRepositoryConvertWorkOrderAsync(currentWo);

            if (result.IsError)
            {
                throw new OneSourceException(result.Message);
            }

            Close();
        }
        private void btnDefer_Click(object sender, EventArgs e)
        {
            var lstSelectedConcerns = _concernPanelControl.LstConcernControls.Where(x => x.IsConcernSelected && !x.Concern.IsDefer).Select(x => x.Concern.ConcernId).ToList();
            var lstSelectedLaborGuides = _concernPanelControl.LstConcernControls.SelectMany(x => x.ListLaborGuideTitle).Where(x => x.IsSelected && !x.IsDefered).Select(x => new DeferedLaborGuideParameter
            {
                LaborTimeId = x.LaborTimeId,
                ConcernId = x.Concern.ConcernId
            }).ToList();
            DeferConcern(lstSelectedConcerns, lstSelectedLaborGuides);
        }
        private async void BtnEstimateEmail_Click(object sender, EventArgs e)
        {
            string mail = string.Empty;
            string fileName = string.Empty;
            Cursor.Current = Cursors.WaitCursor;
            CommercialCustomer customer = _workOrderEntityResult.Customer;
            mail = customer.Email;
            fileName = string.Format("{0}/{1}/{2}_{3}.pdf", PersistentModels.ShopName, "ESTIMATE", customer.FirstName, customer.LastName, DateTime.Now.ToString("ddMMyyyy"));
            if (string.IsNullOrEmpty(mail))
            {
                FrmPopupMail frmPopupMail = new FrmPopupMail();
                frmPopupMail.customer = customer;
                frmPopupMail.ShowDialog();
                mail = frmPopupMail.Mail;

                if (mail == string.Empty || !frmPopupMail.IsSaved)
                {
                    return;
                }
            }

            wBSearching.Text = "Genrating Estimate ...";
            wBSearching.Visible = true;
            wBSearching.StartWaiting();

            decimal amountDiscount;
            decimal.TryParse(lblDiscountTotal.Text, out amountDiscount);

            var totalAmount = Convert.ToDecimal(lblTotal.Text.Replace("$", ""));

            ReportProcessor reportProcessor = new ReportProcessor();
            var report = new Estimate(TotalParts, TotalLabors, TotalSublet, _saleTaxTotal, amountDiscount, totalAmount);
            await report.LoadData(PersistentModels.CurrentWorkOrderId, PersistentModels.CurrentCompany);

            Telerik.Reporting.InstanceReportSource instanceReportSource = new Telerik.Reporting.InstanceReportSource();
            instanceReportSource.ReportDocument = report;
            RenderingResult result = reportProcessor.RenderReport("PDF", instanceReportSource, null);

            var res = await OSRest.UploadFile(new UploadReq
            {
                FileName = fileName,
                Content = result.DocumentBytes
            });

            if (!string.IsNullOrEmpty(PersistentModels.CurrentCompany.EstimateContent))
            {
                if (string.IsNullOrEmpty(mail))
                {
                    RadMessageBox.Show("NO EMAIL TO SEND", "Car Is Ready", MessageBoxButtons.OK, RadMessageIcon.Error);
                    wBSearching.Visible = false;
                    wBSearching.StopWaiting();
                    wBSearching.Text = "Loading , please wait ...";
                    return;
                }

                var notification = new Notification
                {
                    FirstName = _customer.FirstName.ToLower(),
                    LastName = _customer.LastName.ToLower(),
                    Year = txtVinYear.Text,
                    Make = txtVinMake.Text,
                    Model = txtVinModel.Text,
                };

                var linkEstimate = $"<br>Click on the link to view estimate: <a href='{res.FileUrl}'>Click here to view</a>";
                string body = OSHelper.NotificationContent(notification, PersistentModels.CurrentCompany.EstimateContent) + linkEstimate;

                // var resp = await OSRest.SendMail(mail, body, "Estimate");
                wBSearching.Visible = false;
                using (FrmSendEmail frm = new FrmSendEmail(new string[] { customer.Email }))
                {
                    frm.ShowDialog();
                    if (frm.IsSend)
                    {
                        wBSearching.Visible = true;
                        wBSearching.Text = "Estimate is being sent ...";
                        OSRest.SendMultiMail(frm.MailList, body, "Estimate");
                        RadMessageBox.Show("ESTIMATE IS SENT", "Email", MessageBoxButtons.OK, RadMessageIcon.Info);
                    }
                }

            }
            else
            {
                RadMessageBox.Show("NO ESTIMATE CONTENT. PLEASE SET UP IN MARKETING", "Email", MessageBoxButtons.OK, RadMessageIcon.Error);
            }

            wBSearching.Visible = false;
            wBSearching.StopWaiting();
            wBSearching.Text = "Loading , please wait ...";
            Cursor.Current = Cursors.Default;

        }

        private void btnHistory_Click(object sender, EventArgs e)
        {
            if (PersistentModels.CurrentUser.UserGroup.ToString().ToUpper() != UserGroupRole.ADMIN.ToString().ToUpper())
            {
                var permissionListFromDb = FrmWorkOrderPartService.CreateService().SPPermissionRepositoryGetAllAsync(new Permission());
                if (permissionListFromDb.Count > 0)
                {
                    if (!permissionListFromDb.Any(x =>
                        x.ScreenCode == ScreenName.VEHICLE_HISTORY.ToString() &&
                        x.Role.ToLower() == PersistentModels.CurrentUser?.UserGroup.ToLower()))
                    {
                        RadMessageBox.Show("YOU DO NOT HAVE PERMISSION", "PERMISSION", MessageBoxButtons.OK, RadMessageIcon.Error);
                        return;
                    }
                }
            }

            FrmVehicleHistory frmCustomerHistory = new FrmVehicleHistory(_vehicleId);
            frmCustomerHistory.ShowDialog(this);
            ReloadConcerns();
        }
        private void btnOrderParts_Click(object sender, EventArgs e)
        {
            if (_workOrderEntityResult.Type != WorkOrderTypeEnum.WorkOrder.ToString())
            {
                RadMessageBox.Show("Only Work Order can order part. Please convert to Work Order first", "WARNING", MessageBoxButtons.OK, RadMessageIcon.Error);
                return;
            }
            using (var frm = new FrmPartOrder(TypeOrderEnum.WorkOrder, this))
            {
                frm.ShowDialog();
                if (frm.IsHavingOrderedPart)
                {
                    btnReceiveOrderPart.Enabled = true;
                }
            }
        }
        private void btnPartList_Click(object sender, EventArgs e)
        {
            var frm = new FrmPartList();
            frm.ShowDialog(this);
        }
        private void btnPayments_Click(object sender, EventArgs e)
        {
            AddPayment();
        }
        private void btnPleaseCallEmail_Click(object sender, EventArgs e)
        {

            if (!string.IsNullOrEmpty(PersistentModels.CurrentCompany.PleaseCallContent))
            {
                if (string.IsNullOrEmpty(_currentCustomer.Email))
                {
                    RadMessageBox.Show("NO EMAIL TO SEND", "Call content", MessageBoxButtons.OK, RadMessageIcon.Error);
                    return;
                }
                wBSearching.Visible = true;
                wBSearching.StartWaiting();

                var notification = new Notification
                {
                    FirstName = _customer.FirstName.ToLower(),
                    LastName = _customer.LastName.ToLower(),
                    Year = txtVinYear.Text,
                    Make = txtVinMake.Text,
                    Model = txtVinModel.Text,
                };

                var body = OSHelper.NotificationContent(notification, PersistentModels.CurrentCompany.PleaseCallContent);
                wBSearching.Visible = false;

                using (FrmSendEmail frm = new FrmSendEmail(new string[] { _workOrderEntityResult.Customer.Email }))
                {
                    frm.ShowDialog();
                    if (frm.IsSend)
                    {
                        wBSearching.Visible = true;
                        wBSearching.Text = "Email is being sent ...";
                        OSRest.SendMultiMail(frm.MailList, body, "CALL CONTENT");
                        RadMessageBox.Show("Email IS SENT", "Email", MessageBoxButtons.OK, RadMessageIcon.Info);
                    }

                }
                wBSearching.Text = "Loading , please wait ...";
                wBSearching.Visible = false;
                wBSearching.StopWaiting();
            }
            else
            {
                RadMessageBox.Show("NO CALL CONTENT. PLEASE SET UP IN MARKETING", "Email", MessageBoxButtons.OK, RadMessageIcon.Error);
            }

        }
        private async void btnPleaseCallText_Click(object sender, EventArgs e)
        {
            if (string.IsNullOrEmpty(PersistentModels.CurrentCompany.PleaseCallContent))
            {
                RadMessageBox.Show("NO CALL CONTENT. PLEASE SET UP IN MARKETING", "Call content", MessageBoxButtons.OK, RadMessageIcon.Error);
                return;
            }
            if (string.IsNullOrEmpty(_currentCustomer.Phone1))
            {
                RadMessageBox.Show("NO PHONE TO SEND", "Call content", MessageBoxButtons.OK, RadMessageIcon.Error);
                return;
            }
            wBSearching.Visible = true;
            wBSearching.StartWaiting();
            wBSearching.Text = "Loading , please wait ...";

            var notification = new Notification
            {
                FirstName = _customer.FirstName.ToLower(),
                LastName = _customer.LastName.ToLower(),
                Year = txtVinYear.Text,
                Make = txtVinMake.Text,
                Model = txtVinModel.Text,
            };

            var body = OSHelper.NotificationContent(notification, PersistentModels.CurrentCompany.PleaseCallContent);
            wBSearching.Visible = false;

            using (FrmSendSMS frm = new FrmSendSMS(new string[] { _currentCustomer.Phone1 }))
            {
                frm.ShowDialog();
                if (frm.isSend)
                {
                    wBSearching.Visible = true;
                    wBSearching.Text = "SMS is being sent ...";
                    await OSNotification.NotifyMultySMS(frm.PhoneLst, body);
                }
            }

            wBSearching.Visible = false;
            wBSearching.StopWaiting();
            wBSearching.Text = "Loading , please wait ...";

        }

        private async void btnEstimateText_Click(object sender, EventArgs e)
        {
            if (string.IsNullOrEmpty(PersistentModels.CurrentCompany.EstimateContent))
            {
                RadMessageBox.Show("NO Estimate CONTENT. PLEASE SET UP IN MARKETING", "Estimate content", MessageBoxButtons.OK, RadMessageIcon.Error);
                return;
            }

            if (string.IsNullOrEmpty(_currentCustomer.Phone1))
            {
                RadMessageBox.Show("NO PHONE TO SEND", "Estimate content", MessageBoxButtons.OK, RadMessageIcon.Error);
                return;
            }

            wBSearching.Visible = true;
            wBSearching.StartWaiting();
            wBSearching.Text = "Loading , please wait ...";

            decimal amountDiscount;
            decimal.TryParse(lblDiscountTotal.Text, out amountDiscount);

            var totalAmount = Convert.ToDecimal(lblTotal.Text.Replace("$", ""));

            ReportProcessor reportProcessor = new ReportProcessor();
            var report = new Estimate(TotalParts, TotalLabors, TotalSublet, _saleTaxTotal, amountDiscount, totalAmount);
            await report.LoadData(PersistentModels.CurrentWorkOrderId, PersistentModels.CurrentCompany);

            Telerik.Reporting.InstanceReportSource instanceReportSource = new Telerik.Reporting.InstanceReportSource();
            instanceReportSource.ReportDocument = report;
            RenderingResult result = reportProcessor.RenderReport("PDF", instanceReportSource, null);

            CommercialCustomer customer = _workOrderEntityResult.Customer;
            string fileName = string.Format("{0}/{1}/{2}_{3}.pdf", PersistentModels.ShopName, "ESTIMATE", customer.FirstName, customer.LastName, DateTime.Now.ToString("ddMMyyyy"));

            var res = await OSRest.UploadFile(new UploadReq
            {
                FileName = fileName,
                Content = result.DocumentBytes
            });


            var notification = new Notification
            {
                FirstName = _customer.FirstName.ToLower(),
                LastName = _customer.LastName.ToLower(),
                Year = txtVinYear.Text,
                Make = txtVinMake.Text,
                Model = txtVinModel.Text,
            };

            var linkEstimate = $"<br>Click on the link to view estimate: {res.FileUrl}";

            var body = OSHelper.NotificationContent(notification, PersistentModels.CurrentCompany.EstimateContent) + linkEstimate;
            wBSearching.Visible = false;

            using (FrmSendSMS frm = new FrmSendSMS(new string[] { _currentCustomer.Phone1 }))
            {
                frm.ShowDialog();
                if (frm.isSend)
                {
                    wBSearching.Visible = true;
                    wBSearching.Text = "SMS is being sent ...";
                    await OSNotification.NotifyMultySMS(frm.PhoneLst, body);
                }
            }

            EstimateIsReadyText();
            wBSearching.Visible = false;
            wBSearching.StopWaiting();
            wBSearching.Text = "Loading , please wait ...";

        }
        private void BtnPriceParts_Click(object sender, EventArgs e)
        {
            using (var pricePart = new FrmPricingPart(_currentVehicle, string.Empty))
            {
                pricePart.ShowDialog();
                if (pricePart.IsSaveAction)
                {
                    ReloadConcerns();
                }
            }
        }
        private async void BtnPrint_Click(object sender, EventArgs e)
        {
            var report = new RptWorkOrder();
            await report.LoadData(PersistentModels.CurrentWorkOrderId, PersistentModels.CurrentCompany, lblCreditCard.Text);

            var frm = new FrmReportViewer(report);
            frm.ShowDialog();
        }
        private void btnReceiveOrderPart_Click(object sender, EventArgs e)
        {
            var frm = new FrmReceiveOrderPart(TypeOrderEnum.WorkOrder, _currentVehicle, _workOrderNumber, null, LoadDataWorkOrder, this);
            frm.ShowDialog(this);
        }

        private void btnReturnPart_Click(object sender, EventArgs e)
        {
            var f = new FrmMarkPartReturn(null, PersistentModels.CurrentWorkOrderId, true);
            f.ShowDialog();
        }

        private async void btnSave_Click(object sender, EventArgs e)
        {
            wBSearching.Visible = true;
            wBSearching.StartWaiting();
            bool isValid = await SaveWO();
            if (isValid)
            {
                Close();
            }
            wBSearching.Visible = false;
            wBSearching.StopWaiting();
        }
        private void btnUnDefered_Click(object sender, EventArgs e)
        {
            var workOrderRepo = new WorkOrderRepository();
            var lstSelectedUnDeferedConcerns = _concernPanelControl.LstConcernControls.Where(x => x.IsConcernSelected && x.Concern.IsDefer).Select(x => x.Concern).ToList();
            var lstSelectedUnDeferedLaborGuides = _concernPanelControl.LstConcernControls.SelectMany(x => x.ListLaborGuideTitle).Where(x => x.IsSelected && x.IsDefered).Select(x => new UnDeferedLaborGuideParameter
            {
                LaborTimeId = x.LaborTimeId,
                Concern = x.Concern
            }).ToList();
            var workOrderRepository = new WorkOrderRepository();
            var lstSelectedUnDeferedConcernsDto = lstSelectedUnDeferedConcerns;
            UnDeferConcern(lstSelectedUnDeferedConcernsDto, lstSelectedUnDeferedLaborGuides);
        }
        private void btnViewInvoice_Click(object sender, EventArgs e)
        {
            FrmInvoiceInfor frm = new FrmInvoiceInfor(PersistentModels.CurrentWorkOrderId);
            frm.ShowDialog();
        }
        private async void btnShareInfor_Click(object sender, EventArgs e)
        {
            FrmWorkOrderPartService.CreateService().GetUnitOfWork().workOrderRepository.ShareInformation(PersistentModels.CurrentWorkOrderId);
            wBSearching.Visible = true;
            wBSearching.StartWaiting();
            wBSearching.Text = "Loading , please wait ...";

            var notification = new Notification
            {
                FirstName = _customer.FirstName.ToLower(),
                LastName = _customer.LastName.ToLower(),
                Year = txtVinYear.Text,
                Make = txtVinMake.Text,
                Model = txtVinModel.Text,
            };

            string status = "";
            if (drdWorkOrderStatus.Text == WorkOrderStatusEnum.NEEDS_TO_BE_PICKED_UP.GetEnumText())
            {
                string pickupLocation = FrmWorkOrderPartService.CreateService().GetUnitOfWork().workOrderRepository.GetPickupLocation(PersistentModels.CurrentWorkOrderId);
                var body = OSHelper.NotificationContent(notification, OSHelper.NEEDSTOBEPICKEDUP_CONTENT(pickupLocation), true);
                status = await OSNotification.NotifyMultySMS(new List<string> { _customer.Phone1 }, body);
            }
            else if (drdWorkOrderStatus.Text == WorkOrderStatusEnum.WAITING_AUTHORIZATION.GetEnumText())
            {
                var body = OSHelper.NotificationContent(notification, OSHelper.WAITING_AUTHORIZATION_CONTENT, true) + $"\n <a href='https://www.auto1source.com/workorder/{_workOrderEntityResult.Token}'>Click here</a>";
                status = await OSNotification.NotifyMultySMS(new List<string> { _customer.Phone1 }, body);
                OSRest.SendMultiMail(new List<string> { _customer.Email }, body, "[BILL TYSON'S AUTO REPAIR] WAITING AUTHORIZATION");
            }
            else if (drdWorkOrderStatus.Text == WorkOrderStatusEnum.WAITING_AUTHORIZATION.GetEnumText())
            {
                var body = OSHelper.NotificationContent(notification, OSHelper.FINISHED_CONTENT, true);
                status = await OSNotification.NotifyMultySMS(new List<string> { _customer.Phone1 }, body);
                OSRest.SendMultiMail(new List<string> { _customer.Email }, body, "[BILL TYSON'S AUTO REPAIR] FINISHED");
            }

            if (status != "")
            {
                RadMessageBox.Show("SMS can not be sent", "SMS Error", MessageBoxButtons.OK, RadMessageIcon.Error);
            }

            wBSearching.Visible = false;
            wBSearching.StopWaiting();
        }

        private void FrmWorkOrderPart_FormClosed(object sender, FormClosedEventArgs e)
        {
            FrmWorkOrderPartService.CloseService();
        }

        private async void DrdWorkOrderStatus_SelectedValueChanged(object sender, EventArgs e)
        {
            //https://app.asana.com/0/1176431845863857/1205446800594984
            if (drdWorkOrderStatus.Text == WorkOrderStatusEnum.WAITING_AUTHORIZATION.GetEnumText()
           || drdWorkOrderStatus.Text == WorkOrderStatusEnum.FINISHED.GetEnumText()
           )
            {
                if (!PersistentModels.ListPermissionCurrentUserGroup.Any(x =>
                                     x.ScreenCode == ScreenName.TICKET_STATUS.ToString() &&
                                     x.Role.ToLower() == PersistentModels.CurrentUser?.UserGroup.ToLower())
              && PersistentModels.CurrentUser.UserGroup != UserGroupRole.ADMIN.ToString())
                {
                    if (drdWorkOrderStatus.SelectedValue == null || drdWorkOrderStatus.SelectedValue.ToString() != _workOrderEntityResult.WorkOrderStatusName)
                    {
                        RadMessageBox.Show("YOU DO NOT HAVE PERMISSION", "Info", MessageBoxButtons.OK, RadMessageIcon.Info);
                        drdWorkOrderStatus.SelectedValue = _workOrderEntityResult.WorkOrderStatusName;
                        return;
                    }

                }
            }
            if (drdWorkOrderStatus.Text == WorkOrderStatusEnum.PARTS_ON_ORDER.GetEnumText())
            {
                if (!PersistentModels.ListPermissionCurrentUserGroup.Any(x =>
                                     x.ScreenCode == ScreenName.CHANGE_STATUS_TO_PARTS_ON_ORDER.ToString() &&
                                     x.Role.ToLower() == PersistentModels.CurrentUser?.UserGroup.ToLower())
              && PersistentModels.CurrentUser.UserGroup != UserGroupRole.ADMIN.ToString())
                {
                    if (drdWorkOrderStatus.SelectedValue == null || drdWorkOrderStatus.SelectedValue.ToString() != _workOrderEntityResult.WorkOrderStatusName)
                    {
                        RadMessageBox.Show("YOU DO NOT HAVE PERMISSION", "Info", MessageBoxButtons.OK, RadMessageIcon.Info);
                        drdWorkOrderStatus.SelectedValue = _workOrderEntityResult.WorkOrderStatusName;
                        return;
                    }

                }
            }

            if (drdWorkOrderStatus.SelectedItem != null && drdWorkOrderStatus.SelectedItem.Text == WorkOrderStatusEnum.FINISHED.GetEnumText())
            {

                var coreParts = await FrmWorkOrderPartService.CreateService().SPPartRepositoryCorePartAsync(PersistentModels.CurrentWorkOrderId);
                if (coreParts.Count > 0)
                {
                    DialogResult ds = RadMessageBox.Show(this, "Is Core returnable ?", "Core Return", MessageBoxButtons.YesNo, RadMessageIcon.Question);
                    if (ds == DialogResult.No)
                    {
                        List<PickedPart> pps = new List<PickedPart>();
                        foreach (var item in coreParts)
                        {
                            pps.Add(new PickedPart
                            {
                                ConcernId = item.ConcernId,
                                PartId = item.PartId,
                                Quantity = item.Quantity,
                                Amount = item.Cost,
                                Total = item.Cost * item.Quantity
                            });
                        }
                        await FrmWorkOrderPartService.CreateService().SPPickedPartRepositoryAddNewPartsAsync(pps);
                        ReloadConcerns();
                    }
                }
            }

            if (drdWorkOrderStatus.SelectedItem != null && drdWorkOrderStatus.SelectedItem.Text == WorkOrderStatusEnum.NEEDS_TO_BE_DELIVERED.GetEnumText() 
                && _workOrderEntityResult.TotalDue > 0)
            {
                RadMessageBox.Show("THIS STATUS IS ONLY AVAILABLE UNTIL THE WORK ORDER IS PAID", "Info", MessageBoxButtons.OK, RadMessageIcon.Info);
                drdWorkOrderStatus.SelectedValue = _workOrderEntityResult.WorkOrderStatusName;
                return;
            }

        }
        private async void FrmCannedJobListing_PickCannedJob(object sender, int selectedConcernId, string concernValue)
        {
            wBSearching.Visible = true;
            wBSearching.StartWaiting();

            if (sender != null)
            {
                var commonJob = sender as CommonJob;
                var employeeIdForJob = GetEmployeeForJob();
                FrmWorkOrderPartService.CloseService();

                using (var unitOfwork = new DapperUnitOfWork())
                {
                    await unitOfwork.workOrderRepository.PickCanjob(commonJob, employeeIdForJob, _currentVehicle);
                }

                //FrmWorkOrderPartService.CreateService().GetUnitOfWork().workOrderRepository.PickCanjob(commonJob, employeeIdForJob, this._currentVehicle);
                LoadDataWorkOrder();

                wBSearching.Visible = false;
                wBSearching.StopWaiting();
            }
        }

        private async void FrmWorkOrder_FormClosed(object sender, FormClosedEventArgs e)
        {
            var crrentWorkOrderId = PersistentModels.CurrentWorkOrderId;
            PersistentModels.CurrentWorkOrderId = 0;
            await FrmWorkOrderPartService.CreateService().SPWorkOrderRepositoryUpdateIsUsingAsync(crrentWorkOrderId, false);

            SharedComponents.MainForm?.ReloadWorkOrderList();
            _oneSourceAlert.Hide();
        }
        private void Delete_Click(object sender, EventArgs e)
        {
            var allowDelete = FrmWorkOrderPartService.CreateService().SPPermissionRepositoryAllowDeleteWOAsync();
            if (!allowDelete)
            {
                RadMessageBox.Show("You do not have permission", "Permission", MessageBoxButtons.OK, RadMessageIcon.Info);
                return;
            }

            FrmWorkOrderPartService.CreateService().SPWorkOrderRepositoryRemoveAsync(PersistentModels.CurrentWorkOrderId);
            Close();
        }
        private void btnTowingDocument_Click(object sender, EventArgs e)
        {
            FrmTowingDocument frm = new FrmTowingDocument();
            frm.ShowDialog();
        }


        void AddPayment()
        {
            var labor = Convert.ToDecimal(lblLaborTotal.Text.Replace("$", ""));
            var sublet = Convert.ToDecimal(lblSublet.Text.Replace("$", ""));
            var towing = Convert.ToDecimal(lblTowing.Text.Replace("$", ""));
            var parts = Convert.ToDecimal(lblPartsTotal.Text.Replace("$", ""));
            var fei = Convert.ToDecimal(lblFEITaxTotal.Text.Replace("$", ""));
            var saleTax = Convert.ToDecimal(lblSalesTaxTotal.Text.Replace("$", ""));
            var shop = Convert.ToDecimal(lblShopCharge.Text.Replace("$", ""));
            var discount = Convert.ToDecimal(lblDiscountTotal.Text.Replace("$", ""));
            var formula = labor + parts + sublet + towing + fei + saleTax + shop - discount;
            var total = Convert.ToDecimal(lblTotal.Text.Replace("$", ""));
            var creditCardFee = Convert.ToDecimal(lblCreditCard.Text.Replace("$", ""));

            var totalAmount = Convert.ToDecimal(lblTotal.Text.Replace("$", ""));
            // decimal creditCardCon = ((labor + parts + sublet + towing + fei + saleTax + shop) * (PersistentModels.CurrentCompany.CreditCardCharge1 / 100) ?? 0);
            var frm = new FrmAddPayment(_workOrderEntityResult, SetAmountDiscount, CalculateTotal, CloseWorkOrder);
            frm.ShowDialog(this);
            if (frm.CreditCardCharge > 0)
            {
                //lblCreditCardConv.Text = frm.CreditCardCharge.ToStringDecimal();
            }
        }

        async Task<bool> SaveWO()
        {
            //if (drdEmployee.SelectedValue == null || string.IsNullOrWhiteSpace(drdEmployee.SelectedValue?.ToString()) || drdEmployee.SelectedValue?.ToString() == "0")
            //{
            //    RadMessageBox.Show("YOU CAN NOT CHANGE STATUS UNTIL TECHNICIAN IS ASSIGNED", "Save Error", MessageBoxButtons.OK, RadMessageIcon.Error);
            //    return false;
            //}

            int vinYear;
            int.TryParse(txtVinYear.Text, out vinYear);
            int miles = txtVinMiles.Value.GetActualNullableInteger();
            WorkOrder wo = PrepareWorkOrder(vinYear, miles);
            if (drdWorkOrderStatus.Text == WorkOrderStatusEnum.FINISHED.GetEnumText())
            {
                wo.FinishDate = DateTime.Now;
            }
            if (drdWorkOrderStatus.Text == WorkOrderStatusEnum.WAITING_AUTHORIZATION.GetEnumText()
                || drdWorkOrderStatus.Text == WorkOrderStatusEnum.PARTS_ON_ORDER.GetEnumText()
                || drdWorkOrderStatus.Text == WorkOrderStatusEnum.IN_PROGRESS.GetEnumText()
                || drdWorkOrderStatus.Text == WorkOrderStatusEnum.NEEDS_QC.GetEnumText()
                || drdWorkOrderStatus.Text == WorkOrderStatusEnum.FINISHED.GetEnumText()
                || drdWorkOrderStatus.Text == WorkOrderStatusEnum.NEEDS_TO_BE_DELIVERED.GetEnumText()
            )
            {
                wo.IsNotified = true;
            }
            else
            {
                wo.IsNotified = false;
            }

            var result = await FrmWorkOrderPartService.CreateService().SPWorkOrderRepositoryUpdateWorkOrderAsync(wo);

            _validationProvider.IsShowTooltip = false;
            _validationProvider.IsFocus = false;
            if (result.IsError)
            {
                ProcessErrorOccur(result);
                return false;
            }
            else
            {
                if (workOrderType.ToLower() == WorkOrderTypeEnum.Estimate.ToString().ToLower())
                {
                    await OSRest.ReactVinInfor(new
                    {
                        email = _currentCustomer.Email,
                        vehicle = new
                        {
                            year = Convert.ToInt16(txtVinYear.Text),
                            make = txtVinMake.Text,
                            model = txtVinModel.Text,
                            vinNumber = txtVinNumber.Text,
                            lastServicedDate = DateTime.Now,
                            mile = txtVinMiles.Value.GetActualNullableInteger(),
                        },
                        status = "CLOSED",
                        shop = PersistentModels.ShopName
                    });
                }
                else
                {
                    if (_oldVehicle != null && txtVinNumber.Text == _oldVehicle.vinNumber)
                    {
                        _oldVehicle = null;
                    }

                    dynamic requestObject = new
                    {
                        email = _currentCustomer.Email,
                        vehicle = new
                        {
                            year = Convert.ToInt16(txtVinYear.Text),
                            make = txtVinMake.Text,
                            model = txtVinModel.Text,
                            vinNumber = txtVinNumber.Text,
                            bodyStyle = _currentVehicle.BodyStyle,
                            engineType = txtEngineType.Text,
                            tag = txtVinTag.Text,
                            trim = txtTrim.Text,
                            abs = txtVinABS.Text,
                            mile = !string.IsNullOrEmpty(txtVinMiles.Text) ? Convert.ToInt32(txtVinMiles.Text.Replace(",", "")) : 0,
                        },
                        status = drdWorkOrderStatus.Text,
                        shop = PersistentModels.ShopName,
                        workOrderId = _workOrderId,
                        data = new
                        {
                            id = woToken != null ? woToken : string.Empty,
                            type = "WorkOrderDetail"
                        },
                        oldVehicle = _oldVehicle
                    };


                    await OSRest.ReactVinInfor(requestObject);
                }

            }

            if (drdWorkOrderStatus.SelectedItem != null && drdWorkOrderStatus.SelectedItem.Text == WorkOrderStatusEnum.PARTS_ON_ORDER.GetEnumText() &&
                (PersistentModels.ListPermissionCurrentUserGroup.Any(x =>
                                     x.ScreenCode == ScreenName.CHANGE_STATUS_TO_PARTS_ON_ORDER.ToString() &&
                                     x.Role.ToLower() == PersistentModels.CurrentUser?.UserGroup.ToLower())
              || PersistentModels.CurrentUser.UserGroup == UserGroupRole.ADMIN.ToString()))
            {
                DialogResult ds = RadMessageBox.Show(this, "HAS CUSTOMER AUTHORIZED WORK ?", "CUSTOMER INVOICE", MessageBoxButtons.YesNo, RadMessageIcon.Question);
                if (ds == DialogResult.Yes)
                {
                    decimal amountAfterTax = _workOrderEntityResult.AmountAfterTax == null ? 0 : _workOrderEntityResult.AmountAfterTax.Value;
                    string smsContent =
                  $@"Our records indicate that you approved work on your vehicle for the amount of ${amountAfterTax.ToDecimalString()}, did you approve this charge? If yes, please click on the below link: https://www.auto1source.com/pay/{_workOrderEntityResult.Token}";

                    List<string> phones = new List<string> { _workOrderEntityResult.Customer.Phone1 };
                    var smsStatus = await OSNotification.NotifyMultySMS(phones, smsContent);

                    string emailContent =
                  $@"Our records indicate that you approved work on your vehicle for the amount of ${amountAfterTax.ToStringDecimal()}, did you approve this charge? If yes, please <a href='https://www.auto1source.com/pay/{_workOrderEntityResult.Token}'>Click here</a>";

                    //Email
                    var mailRes = await OSRest.SendMail(_workOrderEntityResult.Customer.Email, emailContent, "INVOICE");

                    if (smsStatus != "")
                    {
                        RadMessageBox.Show(smsStatus, "SMS FAIL", MessageBoxButtons.OK, RadMessageIcon.Error);
                    }

                    if (mailRes != null && mailRes.IsError)
                    {
                        RadMessageBox.Show(mailRes.Msg, "EMAIL FAIL", MessageBoxButtons.OK, RadMessageIcon.Error);
                    }
                }

            }
            return true;

        }
    }
}