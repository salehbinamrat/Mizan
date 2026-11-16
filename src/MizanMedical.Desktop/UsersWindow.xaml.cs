using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;

namespace MizanMedical.Desktop;

public partial class UsersWindow : Window
{
    private readonly ObservableCollection<GrantOption> _grants = [];
    private List<DepartmentDto> _departments = [];
    private Guid? _editingId;
    private bool _busy;
    private bool _loading;

    private static readonly Dictionary<string, string> Labels = new()
    {
        ["patients.read"] = "المرضى — عرض",
        ["patients.create"] = "المرضى — إضافة",
        ["services.read"] = "الخدمات والأسعار — عرض",
        ["services.manage"] = "الخدمات والأسعار — إضافة وتعديل",
        ["desks.read"] = "الصناديق — عرض",
        ["desks.manage"] = "الصناديق — إنشاء وإدارة",
        ["shifts.read"] = "المناوبات — عرض",
        ["shifts.open"] = "المناوبات — فتح",
        ["shifts.close"] = "المناوبات — إقفال",
        ["invoices.read"] = "الفواتير — عرض",
        ["invoices.issue"] = "الفواتير — إصدار",
        ["receipts.create"] = "التحصيل — إنشاء سند قبض",
        ["expenses.create"] = "المصروفات — إنشاء سند صرف",
        ["reports.read"] = "التقارير المالية — عرض وتصدير",
        ["audit.read"] = "سجل العمليات — عرض ضمن نطاق القسم"
    };

    public UsersWindow()
    {
        InitializeComponent();
        RoleCombo.ItemsSource = new[]
        {
            new RoleOption("DepartmentManager", "مسؤول قسم"),
            new RoleOption("Receptionist", "استقبال"),
            new RoleOption("Cashier", "أمين صندوق"),
            new RoleOption("Accountant", "محاسب"),
            new RoleOption("Manager", "مسؤول عام"),
            new RoleOption("Admin", "إدارة عامة")
        };
        GrantsList.ItemsSource = _grants;
    }

    private async void Window_Loaded(object sender, RoutedEventArgs e) => await Do(Refresh);
    private async void Refresh_Click(object sender, RoutedEventArgs e) => await Do(Refresh);

    private async Task Refresh()
    {
        _loading = true;
        try
        {
            _departments = await App.Api.Departments();
            DepartmentCombo.ItemsSource = _departments;
            string[] catalog = (await App.Api.PermissionCatalog()).ToArray();
            _grants.Clear();
            foreach (string key in catalog)
                if (Labels.TryGetValue(key, out var label)) _grants.Add(new GrantOption(key, label));
            UsersGrid.ItemsSource = await App.Api.Users();
            ResetForm();
            StatusText.Text = "يمكنك إنشاء حساب لكل قسم وتحديد صلاحياته مستقلة عن الدور الوظيفي.";
        }
        finally { _loading = false; }
    }

    private void ResetForm()
    {
        _loading = true;
        try
        {
            _editingId = null;
            UsersGrid.SelectedItem = null;
            UsernameBox.IsEnabled = true;
            UsernameBox.Clear(); FullNameBox.Clear(); PasswordBox.Clear();
            RoleCombo.SelectedValue = "DepartmentManager";
            AllDepartmentsBox.IsChecked = false;
            DepartmentCombo.SelectedIndex = _departments.Count == 1 ? 0 : -1;
            ActiveBox.IsChecked = true;
            foreach (var grant in _grants) grant.Selected = false;
            SaveUserButton.Content = "إنشاء المستخدم";
        }
        finally { _loading = false; }
    }

    private void NewUser_Click(object sender, RoutedEventArgs e) => ResetForm();

    private void UsersGrid_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_loading || UsersGrid.SelectedItem is not StaffUserDto user) return;
        if (user.IsSystemOwner)
        {
            ResetForm();
            StatusText.Text = "حساب المسؤول الرئيسي محمي من التعديل داخل هذه الصفحة.";
            return;
        }
        _loading = true;
        try
        {
            _editingId = user.Id;
            UsernameBox.Text = user.Username;
            UsernameBox.IsEnabled = false;
            FullNameBox.Text = user.FullName;
            RoleCombo.SelectedValue = user.Role;
            DepartmentCombo.SelectedValue = user.DepartmentId;
            AllDepartmentsBox.IsChecked = user.AllDepartments;
            DepartmentCombo.IsEnabled = !user.AllDepartments;
            ActiveBox.IsChecked = user.IsActive;
            PasswordBox.Clear();
            foreach (var grant in _grants) grant.Selected = user.Permissions.Contains(grant.Key, StringComparer.Ordinal);
            SaveUserButton.Content = "حفظ تعديل الصلاحيات";
        }
        finally { _loading = false; }
    }

    private void ScopeChanged(object sender, RoutedEventArgs e)
    {
        if (DepartmentCombo is null || _loading) return;
        bool all = AllDepartmentsBox.IsChecked == true;
        DepartmentCombo.IsEnabled = !all;
        if (all) DepartmentCombo.SelectedIndex = -1;
    }

    private async void Save_Click(object sender, RoutedEventArgs e) => await Do(async () =>
    {
        bool global = AllDepartmentsBox.IsChecked == true;
        Guid? departmentId = global ? null : DepartmentCombo.SelectedValue is Guid id ? id : null;
        string role = RoleCombo.SelectedValue as string ?? throw new InvalidOperationException("حدد الدور الوظيفي.");
        string[] permissions = _grants.Where(g => g.Selected).Select(g => g.Key).ToArray();
        if (!global && departmentId is null) throw new InvalidOperationException("اختر قسم الموظف.");
        if (_editingId is Guid userId)
        {
            await App.Api.Put<IdResult>($"api/users/{userId}", new
            {
                FullName = FullNameBox.Text, Role = role, DepartmentId = departmentId,
                AllDepartments = global, IsActive = ActiveBox.IsChecked == true, Permissions = permissions
            });
            StatusText.Text = "تم تحديث الصلاحيات وإلغاء جلسات المستخدم السابقة.";
        }
        else
        {
            await App.Api.Post<IdResult>("api/users", new
            {
                Username = UsernameBox.Text, FullName = FullNameBox.Text,
                Password = PasswordBox.Password, Role = role, DepartmentId = departmentId,
                AllDepartments = global, Permissions = permissions
            });
            StatusText.Text = "تم إنشاء المستخدم بنجاح.";
        }
        await Refresh();
    });

    private async void ResetPassword_Click(object sender, RoutedEventArgs e) => await Do(async () =>
    {
        if (_editingId is not Guid id) throw new InvalidOperationException("اختر موظفا موجودا أولا.");
        if (MessageBox.Show(this, "إعادة تعيين كلمة المرور وإغلاق جميع جلسات الموظف؟", "تأكيد",
                MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;
        await App.Api.PostWithoutResult($"api/users/{id}/password", new { Password = PasswordBox.Password });
        PasswordBox.Clear();
        StatusText.Text = "تم تعيين كلمة المرور وإبطال الجلسات السابقة.";
    });

    private async void AddDepartment_Click(object sender, RoutedEventArgs e) => await Do(async () =>
    {
        await App.Api.Post<IdResult>("api/departments", new
        { Code = DepartmentCodeBox.Text.Trim(), Name = DepartmentNameBox.Text.Trim() });
        DepartmentCodeBox.Clear(); DepartmentNameBox.Clear();
        await Refresh();
    });

    private async Task Do(Func<Task> action)
    {
        if (_busy) return;
        _busy = true;
        try { await action(); }
        catch (Exception ex)
        {
            StatusText.Text = ex.Message;
            MessageBox.Show(this, ex.Message, "تعذر تنفيذ العملية", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
        finally { _busy = false; }
    }

    public sealed record RoleOption(string Key, string Name);

    public sealed class GrantOption(string key, string label) : INotifyPropertyChanged
    {
        private bool _selected;
        public string Key { get; } = key;
        public string Label { get; } = label;
        public bool Selected
        {
            get => _selected;
            set { if (_selected == value) return; _selected = value; PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Selected))); }
        }
        public event PropertyChangedEventHandler? PropertyChanged;
    }
}
