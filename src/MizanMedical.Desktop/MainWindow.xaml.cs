using System.Collections.ObjectModel;
using System.Globalization;
using System.IO;
using System.Text;
using System.Windows.Documents;
using Microsoft.Win32;
using System.Windows;
using System.Windows.Controls;
using MizanMedical.Domain;

namespace MizanMedical.Desktop;

public partial class MainWindow : Window
{
    private readonly ObservableCollection<InvoiceLineChoice> _draftLines = [];
    private bool _busy;
    private bool _landingSelected;
    private int _patientsPage = 1;
    private int _invoicesPage = 1;
    private const int PageSize = 100;
    // Reuse the same request ID if the server commits but the network response is lost.
    private Guid _pendingInvoiceId = Guid.NewGuid();
    private Guid _pendingReceiptId = Guid.NewGuid();
    private Guid _pendingExpenseId = Guid.NewGuid();

    private bool Has(string permission) =>
        App.Api.Staff?.IsSystemOwner == true ||
        App.Api.Staff?.Permissions?.Contains(permission, StringComparer.Ordinal) == true;
    private bool CanReport => Has(HospitalPermissions.ReportsRead);

    public MainWindow()
    {
        InitializeComponent();
        DraftLinesGrid.ItemsSource = _draftLines;
        ReportDate.SelectedDate = DateTime.Today;
        ApplyAccountPermissions();
    }

    private void ApplyAccountPermissions()
    {
        var user = App.Api.Staff ?? throw new InvalidOperationException("يجب تسجيل الدخول أولا.");
        var previousTab = ModulesTabs.SelectedItem as TabItem;
        var authorized = LoginNavigationRules.VisibleModules(user.IsSystemOwner, user.Permissions).ToHashSet();
        UserLabel.Text = $"{user.FullName} — {LoginNavigationRules.ArabicRole(user.Role, user.IsSystemOwner)}";

        DashboardTab.Visibility = authorized.Contains(DesktopModule.Dashboard) ? Visibility.Visible : Visibility.Collapsed;
        PatientsTab.Visibility = authorized.Contains(DesktopModule.Patients) ? Visibility.Visible : Visibility.Collapsed;
        ServicesTab.Visibility = authorized.Contains(DesktopModule.Services) ? Visibility.Visible : Visibility.Collapsed;
        InvoicesTab.Visibility = authorized.Contains(DesktopModule.Invoices) ? Visibility.Visible : Visibility.Collapsed;
        CashTab.Visibility = authorized.Contains(DesktopModule.CashDesk) ? Visibility.Visible : Visibility.Collapsed;
        ReceiptsTab.Visibility = authorized.Contains(DesktopModule.Receipts) ? Visibility.Visible : Visibility.Collapsed;
        ExpensesTab.Visibility = authorized.Contains(DesktopModule.Expenses) ? Visibility.Visible : Visibility.Collapsed;
        AdminTab.Visibility = authorized.Contains(DesktopModule.Administration) ? Visibility.Visible : Visibility.Collapsed;

        PatientForm.Visibility = Has(HospitalPermissions.PatientsCreate) ? Visibility.Visible : Visibility.Collapsed;
        ServiceForm.Visibility = Has(HospitalPermissions.ServicesManage) ? Visibility.Visible : Visibility.Collapsed;
        InvoiceForm.Visibility = Has(HospitalPermissions.InvoicesIssue) ? Visibility.Visible : Visibility.Collapsed;
        InvoiceLineForm.Visibility = Has(HospitalPermissions.InvoicesIssue) ? Visibility.Visible : Visibility.Collapsed;
        DraftLinesGrid.Visibility = Has(HospitalPermissions.InvoicesIssue) ? Visibility.Visible : Visibility.Collapsed;
        InvoiceListGroup.Visibility = Has(HospitalPermissions.InvoicesRead) ? Visibility.Visible : Visibility.Collapsed;
        OpenShiftForm.Visibility = Has(HospitalPermissions.ShiftsOpen) ? Visibility.Visible : Visibility.Collapsed;
        CloseShiftForm.Visibility = Has(HospitalPermissions.ShiftsClose) ? Visibility.Visible : Visibility.Collapsed;
        ManageUsersGroup.Visibility = Has(HospitalPermissions.UsersManage) ? Visibility.Visible : Visibility.Collapsed;
        BackupGroup.Visibility = user.IsSystemOwner ? Visibility.Visible : Visibility.Collapsed;

        DesktopModule? first = LoginNavigationRules.LandingModule(user.Role, user.IsSystemOwner, user.Permissions);
        var landingTab = first switch
        {
            DesktopModule.Dashboard => DashboardTab,
            DesktopModule.Patients => PatientsTab,
            DesktopModule.Services => ServicesTab,
            DesktopModule.Invoices => InvoicesTab,
            DesktopModule.CashDesk => CashTab,
            DesktopModule.Receipts => ReceiptsTab,
            DesktopModule.Expenses => ExpensesTab,
            DesktopModule.Administration => AdminTab,
            _ => null
        };
        // Keep the user's current workspace on refresh unless its permission was revoked.
        if (!_landingSelected || previousTab?.Visibility != Visibility.Visible)
            ModulesTabs.SelectedItem = landingTab;
        _landingSelected = true;
    }

    private async void Window_Loaded(object sender, RoutedEventArgs e) => await Perform(RefreshAll);
    private async void Refresh_Click(object sender, RoutedEventArgs e) => await Perform(RefreshAll);

    private async Task RefreshAll()
    {
        await App.Api.RefreshSession();
        ApplyAccountPermissions();
        var account = App.Api.Staff ?? throw new InvalidOperationException("انتهت جلسة الدخول.");

        // Start authorized, independent reads at once to avoid cumulative LAN latency.
        // Every endpoint still enforces its own staff permission and department scope.
        var departmentsTask = App.Api.Departments();
        Task<List<PatientDto>>? patientsTask = Has(HospitalPermissions.PatientsRead)
            ? App.Api.Patients(PatientListSearchBox.Text, _patientsPage, PageSize) : null;
        Task<List<PatientDto>>? invoicePatientsTask =
            Has(HospitalPermissions.InvoicesIssue) && Has(HospitalPermissions.PatientsRead)
            ? App.Api.Patients(InvoicePatientSearchBox.Text) : null;
        Task<List<ServiceDto>>? servicesTask = Has(HospitalPermissions.ServicesRead)
            ? App.Api.Services() : null;
        Task<List<InvoiceDto>>? invoicesTask = Has(HospitalPermissions.InvoicesRead)
            ? App.Api.Invoices(InvoiceListSearchBox.Text, _invoicesPage, PageSize) : null;
        Task<List<InvoiceDto>>? receiptInvoicesTask =
            Has(HospitalPermissions.ReceiptsCreate) && Has(HospitalPermissions.InvoicesRead)
            ? App.Api.Invoices(ReceiptInvoiceSearchBox.Text) : null;
        Task<List<CashDeskDto>>? desksTask = Has(HospitalPermissions.DesksRead)
            ? App.Api.CashDesks() : null;
        Task<List<CashShiftDto>>? shiftsTask = Has(HospitalPermissions.ShiftsRead)
            ? App.Api.OpenShifts() : null;
        Task<DailyReportDto>? reportTask = CanReport
            ? App.Api.Daily(DateOnly.FromDateTime(ReportDate.SelectedDate ?? DateTime.Today))
            : null;

        var requests = new List<Task> { departmentsTask };
        if (patientsTask is not null) requests.Add(patientsTask);
        if (invoicePatientsTask is not null) requests.Add(invoicePatientsTask);
        if (servicesTask is not null) requests.Add(servicesTask);
        if (invoicesTask is not null) requests.Add(invoicesTask);
        if (receiptInvoicesTask is not null) requests.Add(receiptInvoicesTask);
        if (desksTask is not null) requests.Add(desksTask);
        if (shiftsTask is not null) requests.Add(shiftsTask);
        if (reportTask is not null) requests.Add(reportTask);
        await Task.WhenAll(requests);

        var departments = await departmentsTask;
        Guid? previouslySelected = ActiveDepartmentCombo.SelectedValue as Guid?;
        ActiveDepartmentCombo.ItemsSource = departments;
        bool globalScope = account.IsSystemOwner || account.AllDepartments;
        ActiveDepartmentCombo.IsEnabled = globalScope;
        DepartmentScopeLabel.Text = globalScope ? "القسم للإجراءات:" : "قسم المستخدم:";
        ActiveDepartmentCombo.SelectedValue = globalScope
            ? (previouslySelected ?? (departments.Count == 1 ? departments[0].Id : Guid.Empty))
            : account.DepartmentId;

        if (patientsTask is not null)
        {
            var patients = await patientsTask;
            PatientsGrid.ItemsSource = patients;
            PatientPageLabel.Text = $"صفحة {_patientsPage}";
        }
        if (invoicePatientsTask is not null)
            InvoicePatientCombo.ItemsSource = await invoicePatientsTask;
        if (servicesTask is not null)
        {
            var services = await servicesTask;
            ServicesGrid.ItemsSource = services;
            InvoiceServiceCombo.ItemsSource = services;
        }
        if (invoicesTask is not null)
        {
            InvoicesGrid.ItemsSource = await invoicesTask;
            InvoicePageLabel.Text = $"صفحة {_invoicesPage}";
        }
        if (receiptInvoicesTask is not null)
            ReceiptInvoiceCombo.ItemsSource = (await receiptInvoicesTask).Where(i => i.Balance > 0m).ToList();
        if (desksTask is not null)
        {
            OpenDeskCombo.ItemsSource = await desksTask;
            try
            {
                var desk = await App.Api.MyCashDeskSummary();
                PersonalDeskSummaryText.Text = $"صندوقي: {desk.Name} | افتتاح: {desk.OpeningCash:N2} | قبض: {desk.Received:N2} | صرف: {desk.Spent:N2} | الرصيد المتوقع: {desk.Expected:N2}";
            }
            catch (InvalidOperationException)
            {
                PersonalDeskSummaryText.Text = "لا يوجد صندوق شخصي لهذا الحساب؛ راجع مدير البرنامج.";
            }
        }
        if (shiftsTask is not null)
        {
            var shifts = await shiftsTask;
            CloseShiftCombo.ItemsSource = shifts;
            ReceiptShiftCombo.ItemsSource = shifts;
            ExpenseShiftCombo.ItemsSource = shifts;
        }
        if (reportTask is not null) DisplayReport(await reportTask);
        StatusLabel.Text = "تم تحديث البيانات وفق صلاحياتك ونطاق القسم المسموح.";
    }

    private async Task LoadPatientsListPage()
    {
        var patients = await App.Api.Patients(PatientListSearchBox.Text, _patientsPage, PageSize);
        if (_patientsPage > 1 && patients.Count == 0)
        {
            _patientsPage--;
            patients = await App.Api.Patients(PatientListSearchBox.Text, _patientsPage, PageSize);
        }
        PatientsGrid.ItemsSource = patients;
        PatientPageLabel.Text = $"صفحة {_patientsPage}";
    }

    private async Task LoadInvoicesListPage()
    {
        var invoices = await App.Api.Invoices(InvoiceListSearchBox.Text, _invoicesPage, PageSize);
        if (_invoicesPage > 1 && invoices.Count == 0)
        {
            _invoicesPage--;
            invoices = await App.Api.Invoices(InvoiceListSearchBox.Text, _invoicesPage, PageSize);
        }
        InvoicesGrid.ItemsSource = invoices;
        InvoicePageLabel.Text = $"صفحة {_invoicesPage}";
    }

    private async void SearchPatients_Click(object sender, RoutedEventArgs e) =>
        await Perform(async () => { _patientsPage = 1; await LoadPatientsListPage(); });
    private async void NextPatients_Click(object sender, RoutedEventArgs e) =>
        await Perform(async () => { _patientsPage++; await LoadPatientsListPage(); });
    private async void PrevPatients_Click(object sender, RoutedEventArgs e) =>
        await Perform(async () => { if (_patientsPage > 1) _patientsPage--; await LoadPatientsListPage(); });

    private async void SearchInvoices_Click(object sender, RoutedEventArgs e) =>
        await Perform(async () => { _invoicesPage = 1; await LoadInvoicesListPage(); });
    private async void NextInvoices_Click(object sender, RoutedEventArgs e) =>
        await Perform(async () => { _invoicesPage++; await LoadInvoicesListPage(); });
    private async void PrevInvoices_Click(object sender, RoutedEventArgs e) =>
        await Perform(async () => { if (_invoicesPage > 1) _invoicesPage--; await LoadInvoicesListPage(); });

    private async void SearchInvoicePatients_Click(object sender, RoutedEventArgs e) =>
        await Perform(async () =>
        {
            InvoicePatientCombo.ItemsSource = await App.Api.Patients(InvoicePatientSearchBox.Text);
            StatusLabel.Text = "اختر المريض المطلوب من نتائج البحث.";
        });
    private async void SearchReceiptInvoices_Click(object sender, RoutedEventArgs e) =>
        await Perform(async () =>
        {
            var invoices = await App.Api.Invoices(ReceiptInvoiceSearchBox.Text);
            ReceiptInvoiceCombo.ItemsSource = invoices.Where(i => i.Balance > 0m).ToList();
            StatusLabel.Text = "اختر الفاتورة المستحقة من نتائج البحث.";
        });

    private Guid? SelectedDepartment() => ActiveDepartmentCombo.SelectedValue is Guid id && id != Guid.Empty ? id : null;

    private async Task LoadReport()
    {
        var day = DateOnly.FromDateTime(ReportDate.SelectedDate ?? DateTime.Today);
        var report = await App.Api.Daily(day);
        DisplayReport(report);
    }

    private void DisplayReport(DailyReportDto report)
    {
        string currency = report.Currency;
        InvoicedLabel.Text = $"{report.InvoicedAmount:N2} {currency}";
        ReceivedLabel.Text = $"{report.CashReceived:N2} {currency}";
        SpentLabel.Text = $"{report.CashSpent:N2} {currency}";
        NetLabel.Text = $"{report.NetCashMovement:N2} {currency}";
        InvoiceCountLabel.Text = $"عدد الفواتير الصادرة: {report.InvoiceCount}";
    }

    private async void Report_Click(object sender, RoutedEventArgs e) => await Perform(LoadReport);

    private async void ExportCsv_Click(object sender, RoutedEventArgs e) => await Perform(async () =>
    {
        var date = DateOnly.FromDateTime(ReportDate.SelectedDate ?? DateTime.Today);
        var data = await App.Api.Daily(date);
        var save = new SaveFileDialog
        {
            Title = "تصدير ملخص الحسابات",
            FileName = $"MizanMedical-{data.Day}.csv",
            Filter = "CSV لبرنامج Excel (*.csv)|*.csv"
        };
        if (save.ShowDialog(this) != true) return;
        string[] lines =
        [
            "البيان,القيمة",
            $"التاريخ,{Csv(data.Day)}",
            $"العملة,{Csv(data.Currency)}",
            $"عدد الفواتير,{data.InvoiceCount}",
            $"الفواتير الصادرة,{data.InvoicedAmount.ToString(CultureInfo.InvariantCulture)}",
            $"المبالغ المقبوضة,{data.CashReceived.ToString(CultureInfo.InvariantCulture)}",
            $"المصروفات النقدية,{data.CashSpent.ToString(CultureInfo.InvariantCulture)}",
            $"صافي الحركة النقدية,{data.NetCashMovement.ToString(CultureInfo.InvariantCulture)}"
        ];
        await File.WriteAllLinesAsync(save.FileName, lines, new UTF8Encoding(true));
        StatusLabel.Text = "تم تصدير التقرير إلى ملف CSV قابل للفتح في Excel.";
    });

    private async void PrintReport_Click(object sender, RoutedEventArgs e) => await Perform(async () =>
    {
        var date = DateOnly.FromDateTime(ReportDate.SelectedDate ?? DateTime.Today);
        var data = await App.Api.Daily(date);
        var document = new FlowDocument
        {
            FlowDirection = FlowDirection.RightToLeft,
            FontFamily = new System.Windows.Media.FontFamily("Segoe UI"),
            FontSize = 17,
            PagePadding = new Thickness(45),
            ColumnWidth = 650
        };
        document.Blocks.Add(new Paragraph(new Run("ميزان الطبي — التقرير المالي اليومي"))
        { FontSize = 23, FontWeight = FontWeights.Bold });
        document.Blocks.Add(new Paragraph(new Run($"التاريخ: {data.Day} | العملة: {data.Currency}")));
        foreach (string line in new[]
        {
            $"عدد الفواتير: {data.InvoiceCount}",
            $"الفواتير الصادرة: {data.InvoicedAmount:N2}",
            $"التحصيل النقدي: {data.CashReceived:N2}",
            $"الصرف النقدي: {data.CashSpent:N2}",
            $"صافي حركة النقد: {data.NetCashMovement:N2}"
        }) document.Blocks.Add(new Paragraph(new Run(line)));
        document.Blocks.Add(new Paragraph(new Run(data.Note)) { FontSize = 12 });
        var dialog = new PrintDialog();
        if (dialog.ShowDialog() == true)
            dialog.PrintDocument(((IDocumentPaginatorSource)document).DocumentPaginator, "ميزان الطبي - تقرير مالي");
    });

    private static string Csv(string value)
    {
        value = value.Trim();
        // Neutralize Excel formula injection in text cells from server configuration.
        if (value.Length > 0 && "=+@-".Contains(value[0])) value = "'" + value;
        return "\"" + value.Replace("\"", "\"\"") + "\"";
    }

    private async void AddPatient_Click(object sender, RoutedEventArgs e) => await Perform(async () =>
    {
        await App.Api.Post<IdResult>("api/patients", new
        { FileNumber = PatientFileBox.Text.Trim(), FullName = PatientNameBox.Text.Trim(), DepartmentId = SelectedDepartment() });
        PatientFileBox.Clear(); PatientNameBox.Clear();
        await RefreshAll();
        StatusLabel.Text = "تم تسجيل المريض بنجاح.";
    });

    private async void AddService_Click(object sender, RoutedEventArgs e) => await Perform(async () =>
    {
        await App.Api.Post<IdResult>("api/services", new
        {
            Code = ServiceCodeBox.Text.Trim(), Name = ServiceNameBox.Text.Trim(),
            Department = "", Price = Amount(ServicePriceBox.Text), DepartmentId = SelectedDepartment()
        });
        ServiceCodeBox.Clear(); ServiceNameBox.Clear(); ServicePriceBox.Clear();
        await RefreshAll();
        StatusLabel.Text = "تمت إضافة الخدمة.";
    });

    private void AddInvoiceLine_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            if (InvoiceServiceCombo.SelectedItem is not ServiceDto service)
                throw new InvalidOperationException("اختر الخدمة أولا.");
            if (!int.TryParse(InvoiceQtyBox.Text, NumberStyles.Integer, CultureInfo.InvariantCulture, out int quantity) || quantity is < 1 or > 1000)
                throw new InvalidOperationException("الكمية يجب أن تكون بين 1 و1000.");
            if (_draftLines.Any(x => x.ServiceId == service.Id))
                throw new InvalidOperationException("الخدمة مضافة بالفعل، احذف السطر لتعديل الكمية.");
            _draftLines.Add(new InvoiceLineChoice(service.Id, service.Name, quantity, service.Price, quantity * service.Price));
        }
        catch (Exception ex) { MessageBox.Show(this, ex.Message, "تحقق من البيانات", MessageBoxButton.OK, MessageBoxImage.Warning); }
    }

    private void RemoveInvoiceLine_Click(object sender, RoutedEventArgs e)
    {
        if (DraftLinesGrid.SelectedItem is InvoiceLineChoice line) _draftLines.Remove(line);
    }

    private async void CreateInvoice_Click(object sender, RoutedEventArgs e) => await Perform(async () =>
    {
        if (_draftLines.Count == 0) throw new InvalidOperationException("أضف خدمة واحدة على الأقل للفاتورة.");
        var input = new
        {
            RequestId = _pendingInvoiceId, PatientId = SelectedId(InvoicePatientCombo),
            Lines = _draftLines.Select(x => new { x.ServiceId, x.Quantity }).ToArray()
        };
        var invoice = await App.Api.Post<InvoiceCreated>("api/invoices", input);
        _draftLines.Clear();
        _pendingInvoiceId = Guid.NewGuid();
        await RefreshAll();
        MessageBox.Show(this, $"صدرت الفاتورة رقم {invoice.InvoiceNumber}\nالإجمالي: {invoice.Total:N2}", "فاتورة جديدة", MessageBoxButton.OK, MessageBoxImage.Information);
    });

    private async void OpenShift_Click(object sender, RoutedEventArgs e) => await Perform(async () =>
    {
        await App.Api.Post<IdResult>("api/shifts/open", new
        { CashDeskId = SelectedId(OpenDeskCombo), OpeningCash = NonnegativeAmount(OpeningCashBox.Text) });
        await RefreshAll();
        StatusLabel.Text = "تم فتح مناوبة الصندوق.";
    });

    private async void CloseShift_Click(object sender, RoutedEventArgs e) => await Perform(async () =>
    {
        if (MessageBox.Show(this, "هل أكملت عد النقدية وتريد إغلاق المناوبة؟", "تأكيد الإقفال", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes)
            return;
        var shift = await App.Api.Post<ShiftClosed>("api/shifts/close", new
        {
            ShiftId = SelectedId(CloseShiftCombo), CountedCash = NonnegativeAmount(CountedCashBox.Text),
            Comment = CloseCommentBox.Text.Trim()
        });
        await RefreshAll();
        MessageBox.Show(this, $"الرصيد المتوقع: {shift.ExpectedCash:N2}\nالنقد الفعلي: {shift.CountedCash:N2}\nالفرق: {shift.Variance:N2}", "نتيجة الإقفال", MessageBoxButton.OK, MessageBoxImage.Information);
    });

    private async void CreateReceipt_Click(object sender, RoutedEventArgs e) => await Perform(async () =>
    {
        if (MessageBox.Show(this, "تأكيد تسجيل المبلغ المقبوض؟", "تأكيد سند القبض", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes)
            return;
        await App.Api.Post<IdResult>("api/receipts", new
        {
            RequestId = _pendingReceiptId, InvoiceId = SelectedId(ReceiptInvoiceCombo),
            ShiftId = SelectedId(ReceiptShiftCombo), Amount = Amount(ReceiptAmountBox.Text)
        });
        ReceiptAmountBox.Clear();
        _pendingReceiptId = Guid.NewGuid();
        await RefreshAll();
        StatusLabel.Text = "تم تسجيل سند القبض وترحيله إلى الحسابات.";
    });

    private async void AddExpense_Click(object sender, RoutedEventArgs e) => await Perform(async () =>
    {
        if (MessageBox.Show(this, "هل تم اعتماد هذا الصرف وتريد تسجيله؟", "تأكيد سند الصرف", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes)
            return;
        await App.Api.Post<IdResult>("api/expenses", new
        {
            RequestId = _pendingExpenseId, ShiftId = SelectedId(ExpenseShiftCombo),
            Category = ExpenseCategoryBox.Text.Trim(), Description = ExpenseDescriptionBox.Text.Trim(),
            Amount = Amount(ExpenseAmountBox.Text)
        });
        ExpenseDescriptionBox.Clear(); ExpenseAmountBox.Clear();
        _pendingExpenseId = Guid.NewGuid();
        await RefreshAll();
        StatusLabel.Text = "تم تسجيل سند الصرف وترحيله إلى الحسابات.";
    });

    private void Backup_Click(object sender, RoutedEventArgs e)
    {
        new BackupWindow { Owner = this }.ShowDialog();
    }

    private void ManageUsers_Click(object sender, RoutedEventArgs e)
    {
        new UsersWindow { Owner = this }.ShowDialog();
    }

    private async void ChangePassword_Click(object sender, RoutedEventArgs e)
    {
        var change = new ChangePasswordWindow { Owner = this };
        if (change.ShowDialog() != true) return;
        try { await App.Api.Logout(); }
        catch { /* The password change already revoked this session on the server. */ }
        var login = new LoginWindow();
        Application.Current.MainWindow = login;
        login.Show();
        Close();
    }

    private async void Logout_Click(object sender, RoutedEventArgs e)
    {
        try { await App.Api.Logout(); }
        catch (Exception ex) { MessageBox.Show(this, "تعذر إغلاق الجلسة بالخادم: " + ex.Message, "تنبيه"); }
        Application.Current.MainWindow = new LoginWindow();
        Application.Current.MainWindow.Show();
        Close();
    }

    private static Guid SelectedId(ComboBox combo) => combo.SelectedValue is Guid id && id != Guid.Empty
        ? id : throw new InvalidOperationException("اختر قيمة من القائمة أولا.");

    private static decimal Amount(string value)
    {
        if (!decimal.TryParse(value, NumberStyles.Number, CultureInfo.InvariantCulture, out decimal amount)
            || amount <= 0 || decimal.Round(amount, 2) != amount)
            throw new InvalidOperationException("أدخل مبلغا موجبا صحيحا بخانتين عشريتين على الأكثر، باستخدام أرقام إنجليزية.");
        return amount;
    }

    private static decimal NonnegativeAmount(string value)
    {
        if (!decimal.TryParse(value, NumberStyles.Number, CultureInfo.InvariantCulture, out decimal amount)
            || amount < 0 || decimal.Round(amount, 2) != amount)
            throw new InvalidOperationException("أدخل مبلغا صحيحا غير سالب باستخدام أرقام إنجليزية.");
        return amount;
    }

    private async Task Perform(Func<Task> operation)
    {
        if (_busy) return;
        _busy = true;
        StatusLabel.Text = "جار تنفيذ العملية...";
        try { await operation(); }
        catch (Exception ex)
        {
            StatusLabel.Text = "تعذر التأكد من نتيجة العملية. تحقق من السجل قبل إعادة إرسال أي تحصيل أو صرف.";
            MessageBox.Show(this, ex.Message + "\n\nإذا انقطع الاتصال بعد الإرسال، تحقق من الفواتير والتقارير قبل إعادة العملية.",
                "تنبيه النظام", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
        finally { _busy = false; }
    }

    private void Grid_AutoGeneratingColumn(object? sender, DataGridAutoGeneratingColumnEventArgs e)
    {
        if (e.PropertyName is "Id" or "Display" or "ServiceId" or "CashDeskId" or "IssuedAtUtc" or "OpenedAtUtc")
        {
            e.Cancel = true;
            return;
        }
        var headers = new Dictionary<string, string>
        {
            ["FileNumber"] = "رقم الملف", ["FullName"] = "اسم المريض", ["Code"] = "الرمز",
            ["Name"] = "الخدمة", ["Department"] = "القسم", ["Price"] = "السعر",
            ["InvoiceNumber"] = "رقم الفاتورة", ["Total"] = "الإجمالي", ["Paid"] = "المحصل",
            ["Balance"] = "المتبقي", ["PatientName"] = "المريض", ["ServiceName"] = "الخدمة",
            ["Quantity"] = "الكمية", ["UnitPrice"] = "سعر الوحدة"
        };
        if (headers.TryGetValue(e.PropertyName, out string? title)) e.Column.Header = title;
    }

    public sealed record InvoiceLineChoice(Guid ServiceId, string ServiceName, int Quantity, decimal UnitPrice, decimal Total);
}
