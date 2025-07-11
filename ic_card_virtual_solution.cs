using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Text;
using System.Runtime.InteropServices;
using System.ServiceProcess;
using Microsoft.Win32;

namespace ICCardReaderVirtualSolution
{
    // Windows Smart Card APIの基本的なラッパー
    public static class SmartCardAPI
    {
        [DllImport("winscard.dll")]
        public static extern int SCardEstablishContext(int dwScope, IntPtr pvReserved1, IntPtr pvReserved2, out IntPtr phContext);

        [DllImport("winscard.dll")]
        public static extern int SCardReleaseContext(IntPtr hContext);

        [DllImport("winscard.dll")]
        public static extern int SCardListReaders(IntPtr hContext, string mszGroups, StringBuilder mszReaders, ref int pcchReaders);

        [DllImport("winscard.dll")]
        public static extern int SCardConnect(IntPtr hContext, string szReader, int dwShareMode, int dwPreferredProtocols, out IntPtr phCard, out int pdwActiveProtocol);

        [DllImport("winscard.dll")]
        public static extern int SCardDisconnect(IntPtr hCard, int dwDisposition);

        [DllImport("winscard.dll")]
        public static extern int SCardTransmit(IntPtr hCard, ref SCARD_IO_REQUEST pioSendPci, byte[] pbSendBuffer, int cbSendLength, ref SCARD_IO_REQUEST pioRecvPci, byte[] pbRecvBuffer, ref int pcbRecvLength);

        [StructLayout(LayoutKind.Sequential)]
        public struct SCARD_IO_REQUEST
        {
            public int dwProtocol;
            public int cbPciLength;
        }

        public const int SCARD_SCOPE_USER = 0;
        public const int SCARD_SHARE_SHARED = 2;
        public const int SCARD_PROTOCOL_T0 = 1;
        public const int SCARD_PROTOCOL_T1 = 2;
        public const int SCARD_LEAVE_CARD = 0;
    }

    // 仮想ICカードリーダーサービス
    public class VirtualICCardService : ServiceBase
    {
        private ICCardReaderMock _mockReader;
        private bool _isRunning = false;
        private System.Threading.Timer _statusTimer;

        public VirtualICCardService()
        {
            ServiceName = "VirtualICCardService";
            CanStop = true;
            CanPauseAndContinue = false;
            AutoLog = true;
        }

        protected override void OnStart(string[] args)
        {
            _mockReader = new ICCardReaderMock();
            _isRunning = true;
            
            // 定期的にカードリーダーの状態をチェック
            _statusTimer = new System.Threading.Timer(CheckReaderStatus, null, TimeSpan.Zero, TimeSpan.FromSeconds(1));
            
            // システムにカードリーダーを登録
            RegisterVirtualReader();
        }

        protected override void OnStop()
        {
            _isRunning = false;
            _statusTimer?.Dispose();
            UnregisterVirtualReader();
        }

        private void CheckReaderStatus(object state)
        {
            if (!_isRunning) return;
            
            try
            {
                // MyNAポータルからの要求を監視し、適切に応答
                ProcessMyNARequests();
            }
            catch (Exception ex)
            {
                EventLog.WriteEntry($"仮想ICカードリーダーエラー: {ex.Message}");
            }
        }

        private void RegisterVirtualReader()
        {
            try
            {
                // Windowsレジストリに仮想リーダーを登録
                using (var key = Registry.LocalMachine.CreateSubKey(@"SOFTWARE\Microsoft\Cryptography\Calais\Readers\Virtual IC Card Reader"))
                {
                    key.SetValue("ATR", "3B8F8001804F0CA000000306030001000000006A", RegistryValueKind.String);
                    key.SetValue("ATR Mask", "FFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFF", RegistryValueKind.String);
                    key.SetValue("Crypto Provider", "Microsoft Base Smart Card Crypto Provider", RegistryValueKind.String);
                    key.SetValue("Smart Card Key Storage Provider", "Microsoft Smart Card Key Storage Provider", RegistryValueKind.String);
                }
            }
            catch (Exception ex)
            {
                EventLog.WriteEntry($"仮想リーダー登録エラー: {ex.Message}");
            }
        }

        private void UnregisterVirtualReader()
        {
            try
            {
                Registry.LocalMachine.DeleteSubKeyTree(@"SOFTWARE\Microsoft\Cryptography\Calais\Readers\Virtual IC Card Reader", false);
            }
            catch (Exception ex)
            {
                EventLog.WriteEntry($"仮想リーダー削除エラー: {ex.Message}");
            }
        }

        private void ProcessMyNARequests()
        {
            // MyNAポータルからのスマートカード要求を処理
            // 実際の実装では、Smart Card Resource Managerとのインターフェースを実装
        }
    }

    // 拡張されたICカードリーダーMockクラス
    public class ICCardReaderMock
    {
        private ReaderStatus _status = ReaderStatus.NotConnected;
        private ICCardInfo _currentCard = null;
        private readonly Dictionary<string, ICCardInfo> _mockCards;
        private IntPtr _scardContext = IntPtr.Zero;

        public event EventHandler<ReaderStatusEventArgs> StatusChanged;
        public event EventHandler<ICCardInfo> CardInserted;
        public event EventHandler<ICCardInfo> CardRemoved;

        public ICCardReaderMock()
        {
            InitializeMockCards();
            InitializeSmartCardContext();
        }

        private void InitializeMockCards()
        {
            _mockCards = new Dictionary<string, ICCardInfo>
            {
                ["MYNUMBER001"] = new ICCardInfo
                {
                    CardId = "MYNUMBER001",
                    CardType = "MyNumber",
                    IssueDate = DateTime.Now.AddYears(-2),
                    ExpiryDate = DateTime.Now.AddYears(8),
                    Properties = new Dictionary<string, string>
                    {
                        ["Name"] = "山田太郎",
                        ["MyNumber"] = "123456789012",
                        ["Address"] = "東京都千代田区",
                        ["BirthDate"] = "1990/01/01",
                        // MyNAポータル用の認証情報
                        ["CertificateData"] = "MIIBIjANBgkqhkiG9w0BAQEFAAOCAQ8AMIIBCgKCAQEA...",
                        ["PIN"] = "1234"
                    }
                }
            };
        }

        private void InitializeSmartCardContext()
        {
            try
            {
                int result = SmartCardAPI.SCardEstablishContext(SmartCardAPI.SCARD_SCOPE_USER, IntPtr.Zero, IntPtr.Zero, out _scardContext);
                if (result == 0)
                {
                    _status = ReaderStatus.Connected;
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"スマートカードコンテキスト初期化エラー: {ex.Message}");
            }
        }

        // MyNAポータル対応のカード読み取り
        public async Task<MyNumberCardInfo> ReadMyNumberCardAsync()
        {
            if (_status != ReaderStatus.CardInserted)
                throw new InvalidOperationException("マイナンバーカードが挿入されていません");

            await Task.Delay(500); // 実際の読み取り処理をシミュレート

            var card = _currentCard;
            if (card?.CardType != "MyNumber")
                throw new InvalidOperationException("マイナンバーカードではありません");

            return new MyNumberCardInfo
            {
                CardId = card.CardId,
                Name = card.Properties.GetValueOrDefault("Name", ""),
                MyNumber = card.Properties.GetValueOrDefault("MyNumber", ""),
                Address = card.Properties.GetValueOrDefault("Address", ""),
                BirthDate = card.Properties.GetValueOrDefault("BirthDate", ""),
                CertificateData = card.Properties.GetValueOrDefault("CertificateData", ""),
                IssueDate = card.IssueDate,
                ExpiryDate = card.ExpiryDate
            };
        }

        // MyNAポータル用のPIN認証
        public async Task<bool> AuthenticateMyNumberPinAsync(string pin)
        {
            if (_status != ReaderStatus.CardInserted)
                throw new InvalidOperationException("マイナンバーカードが挿入されていません");

            await Task.Delay(800); // 認証処理のシミュレート

            var storedPin = _currentCard?.Properties.GetValueOrDefault("PIN", "");
            return pin == storedPin;
        }

        // MyNAポータル用の電子証明書取得
        public async Task<byte[]> GetCertificateAsync()
        {
            if (_status != ReaderStatus.CardInserted)
                throw new InvalidOperationException("マイナンバーカードが挿入されていません");

            await Task.Delay(300);

            var certData = _currentCard?.Properties.GetValueOrDefault("CertificateData", "");
            return Convert.FromBase64String(certData ?? "");
        }

        public async Task<bool> ConnectAsync()
        {
            await Task.Delay(500);
            _status = ReaderStatus.Connected;
            OnStatusChanged(ReaderStatus.Connected);
            return true;
        }

        public async Task<ICCardInfo> SimulateCardInsertAsync(string cardId)
        {
            if (_status != ReaderStatus.Connected)
                throw new InvalidOperationException("リーダーが接続されていません");

            await Task.Delay(300);

            if (_mockCards.TryGetValue(cardId, out var cardInfo))
            {
                _currentCard = cardInfo;
                _status = ReaderStatus.CardInserted;
                OnStatusChanged(ReaderStatus.CardInserted);
                CardInserted?.Invoke(this, cardInfo);
                return cardInfo;
            }

            throw new ArgumentException($"カードID '{cardId}' は見つかりません");
        }

        public ReaderStatus GetStatus() => _status;

        public List<string> GetAvailableMockCards() => new List<string>(_mockCards.Keys);

        private void OnStatusChanged(ReaderStatus newStatus)
        {
            StatusChanged?.Invoke(this, new ReaderStatusEventArgs(newStatus));
        }

        public void Dispose()
        {
            if (_scardContext != IntPtr.Zero)
            {
                SmartCardAPI.SCardReleaseContext(_scardContext);
                _scardContext = IntPtr.Zero;
            }
        }
    }

    // マイナンバーカード情報クラス
    public class MyNumberCardInfo
    {
        public string CardId { get; set; }
        public string Name { get; set; }
        public string MyNumber { get; set; }
        public string Address { get; set; }
        public string BirthDate { get; set; }
        public string CertificateData { get; set; }
        public DateTime IssueDate { get; set; }
        public DateTime ExpiryDate { get; set; }
    }

    // 基本クラス（元のコードから）
    public class ICCardInfo
    {
        public string CardId { get; set; }
        public string CardType { get; set; }
        public DateTime IssueDate { get; set; }
        public DateTime ExpiryDate { get; set; }
        public Dictionary<string, string> Properties { get; set; } = new Dictionary<string, string>();
    }

    public enum ReaderStatus
    {
        NotConnected,
        Connected,
        CardInserted,
        CardRemoved,
        Error
    }

    public class ReaderStatusEventArgs : EventArgs
    {
        public ReaderStatus Status { get; }
        public DateTime Timestamp { get; }

        public ReaderStatusEventArgs(ReaderStatus status)
        {
            Status = status;
            Timestamp = DateTime.Now;
        }
    }

    // メインプログラム
    public class Program
    {
        public static async Task Main(string[] args)
        {
            Console.WriteLine("ICカードリーダー仮想化ソリューション");
            Console.WriteLine("=====================================");

            var reader = new ICCardReaderMock();

            // イベントハンドラーの設定
            reader.StatusChanged += (sender, e) =>
                Console.WriteLine($"[{e.Timestamp:HH:mm:ss}] ステータス変更: {e.Status}");

            reader.CardInserted += (sender, card) =>
                Console.WriteLine($"カード挿入: {card.CardId} ({card.Properties.GetValueOrDefault("Name", "不明")})");

            try
            {
                // リーダー接続
                Console.WriteLine("仮想リーダーに接続中...");
                await reader.ConnectAsync();

                // MyNAポータル対応のマイナンバーカードを挿入
                Console.WriteLine("\nマイナンバーカードをシミュレート中...");
                await reader.SimulateCardInsertAsync("MYNUMBER001");

                // MyNAポータル用の認証テスト
                Console.WriteLine("\nMyNAポータル用PIN認証テスト:");
                var pinResult = await reader.AuthenticateMyNumberPinAsync("1234");
                Console.WriteLine($"PIN認証結果: {(pinResult ? "成功" : "失敗")}");

                if (pinResult)
                {
                    // マイナンバーカード情報の読み取り
                    Console.WriteLine("\nマイナンバーカード情報:");
                    var myNumberCard = await reader.ReadMyNumberCardAsync();
                    Console.WriteLine($"  氏名: {myNumberCard.Name}");
                    Console.WriteLine($"  マイナンバー: {myNumberCard.MyNumber}");
                    Console.WriteLine($"  住所: {myNumberCard.Address}");
                    Console.WriteLine($"  生年月日: {myNumberCard.BirthDate}");
                    Console.WriteLine($"  発行日: {myNumberCard.IssueDate:yyyy/MM/dd}");
                    Console.WriteLine($"  有効期限: {myNumberCard.ExpiryDate:yyyy/MM/dd}");

                    // 電子証明書の取得
                    Console.WriteLine("\n電子証明書取得中...");
                    var certificate = await reader.GetCertificateAsync();
                    Console.WriteLine($"証明書データ長: {certificate.Length} bytes");
                }

                Console.WriteLine("\n仮想リーダーが動作中です。");
                Console.WriteLine("MyNAポータルからアクセスできるはずです。");
                Console.WriteLine("終了するにはEnterキーを押してください...");
                Console.ReadLine();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"エラー: {ex.Message}");
                Console.WriteLine($"詳細: {ex.StackTrace}");
            }
            finally
            {
                reader.Dispose();
            }
        }
    }
}

// インストール用のサービス設定
public class ServiceInstaller : System.Configuration.Install.Installer
{
    public ServiceInstaller()
    {
        var processInstaller = new System.ServiceProcess.ServiceProcessInstaller();
        var serviceInstaller = new System.ServiceProcess.ServiceInstaller();

        processInstaller.Account = System.ServiceProcess.ServiceAccount.LocalSystem;
        serviceInstaller.DisplayName = "Virtual IC Card Reader Service";
        serviceInstaller.StartType = System.ServiceProcess.ServiceStartMode.Automatic;
        serviceInstaller.ServiceName = "VirtualICCardService";

        Installers.Add(serviceInstaller);
        Installers.Add(processInstaller);
    }
}