using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Text;

namespace ICCardReaderMock
{
    // ICカードの基本情報を表すクラス
    public class ICCardInfo
    {
        public string CardId { get; set; }
        public string CardType { get; set; }
        public DateTime IssueDate { get; set; }
        public DateTime ExpiryDate { get; set; }
        public Dictionary<string, string> Properties { get; set; } = new Dictionary<string, string>();
    }

    // カードリーダーの状態を表す列挙型
    public enum ReaderStatus
    {
        NotConnected,
        Connected,
        CardInserted,
        CardRemoved,
        Error
    }

    // ICカードリーダーのMockクラス
    public class ICCardReaderMock
    {
        private ReaderStatus _status = ReaderStatus.NotConnected;
        private ICCardInfo _currentCard = null;
        private readonly Dictionary<string, ICCardInfo> _mockCards;

        public event EventHandler<ReaderStatusEventArgs> StatusChanged;
        public event EventHandler<ICCardInfo> CardInserted;
        public event EventHandler<ICCardInfo> CardRemoved;

        public ICCardReaderMock()
        {
            InitializeMockCards();
        }

        private void InitializeMockCards()
        {
            _mockCards = new Dictionary<string, ICCardInfo>
            {
                ["CARD001"] = new ICCardInfo
                {
                    CardId = "CARD001",
                    CardType = "FeliCa",
                    IssueDate = DateTime.Now.AddYears(-2),
                    ExpiryDate = DateTime.Now.AddYears(8),
                    Properties = new Dictionary<string, string>
                    {
                        ["Name"] = "山田太郎",
                        ["Company"] = "サンプル会社",
                        ["Department"] = "IT部門",
                        ["EmployeeId"] = "EMP001"
                    }
                },
                ["CARD002"] = new ICCardInfo
                {
                    CardId = "CARD002",
                    CardType = "MIFARE",
                    IssueDate = DateTime.Now.AddYears(-1),
                    ExpiryDate = DateTime.Now.AddYears(4),
                    Properties = new Dictionary<string, string>
                    {
                        ["Name"] = "佐藤花子",
                        ["Company"] = "テスト株式会社",
                        ["Department"] = "営業部",
                        ["EmployeeId"] = "EMP002"
                    }
                }
            };
        }

        // リーダーの接続をシミュレート
        public async Task<bool> ConnectAsync()
        {
            await Task.Delay(500); // 接続処理のシミュレート
            _status = ReaderStatus.Connected;
            OnStatusChanged(ReaderStatus.Connected);
            return true;
        }

        // リーダーの切断をシミュレート
        public async Task DisconnectAsync()
        {
            await Task.Delay(200);
            _status = ReaderStatus.NotConnected;
            _currentCard = null;
            OnStatusChanged(ReaderStatus.NotConnected);
        }

        // カードの挿入をシミュレート
        public async Task<ICCardInfo> SimulateCardInsertAsync(string cardId)
        {
            if (_status != ReaderStatus.Connected)
                throw new InvalidOperationException("リーダーが接続されていません");

            await Task.Delay(300); // カード読み取り処理のシミュレート

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

        // カードの抜き取りをシミュレート
        public async Task SimulateCardRemoveAsync()
        {
            if (_currentCard == null)
                return;

            await Task.Delay(100);
            var removedCard = _currentCard;
            _currentCard = null;
            _status = ReaderStatus.CardRemoved;
            OnStatusChanged(ReaderStatus.CardRemoved);
            CardRemoved?.Invoke(this, removedCard);

            // 少し後に通常の接続状態に戻す
            await Task.Delay(500);
            _status = ReaderStatus.Connected;
            OnStatusChanged(ReaderStatus.Connected);
        }

        // カード情報の読み取り
        public async Task<ICCardInfo> ReadCardAsync()
        {
            if (_status != ReaderStatus.CardInserted)
                throw new InvalidOperationException("カードが挿入されていません");

            await Task.Delay(200); // 読み取り処理のシミュレート
            return _currentCard;
        }

        // カードへのデータ書き込み（シミュレート）
        public async Task<bool> WriteCardAsync(string key, string value)
        {
            if (_status != ReaderStatus.CardInserted)
                throw new InvalidOperationException("カードが挿入されていません");

            await Task.Delay(400); // 書き込み処理のシミュレート
            _currentCard.Properties[key] = value;
            return true;
        }

        // 現在のステータスを取得
        public ReaderStatus GetStatus()
        {
            return _status;
        }

        // 利用可能なモックカードのリストを取得
        public List<string> GetAvailableMockCards()
        {
            return new List<string>(_mockCards.Keys);
        }

        // カードの認証をシミュレート
        public async Task<bool> AuthenticateAsync(string pin)
        {
            if (_status != ReaderStatus.CardInserted)
                throw new InvalidOperationException("カードが挿入されていません");

            await Task.Delay(800); // 認証処理のシミュレート
            
            // 簡単な認証シミュレート（実際の実装では適切な認証処理を行う）
            return pin == "1234" || pin == "0000";
        }

        private void OnStatusChanged(ReaderStatus newStatus)
        {
            StatusChanged?.Invoke(this, new ReaderStatusEventArgs(newStatus));
        }
    }

    // イベント引数クラス
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

    // 使用例のクラス
    public class Program
    {
        public static async Task Main(string[] args)
        {
            var reader = new ICCardReaderMock();
            
            // イベントハンドラーの設定
            reader.StatusChanged += (sender, e) => 
                Console.WriteLine($"[{e.Timestamp:HH:mm:ss}] ステータス変更: {e.Status}");
            
            reader.CardInserted += (sender, card) => 
                Console.WriteLine($"カード挿入: {card.CardId} ({card.Properties.GetValueOrDefault("Name", "不明")})");
            
            reader.CardRemoved += (sender, card) => 
                Console.WriteLine($"カード抜き取り: {card.CardId}");

            try
            {
                // リーダー接続
                Console.WriteLine("リーダーに接続中...");
                await reader.ConnectAsync();

                // 利用可能なカードを表示
                Console.WriteLine("利用可能なモックカード:");
                foreach (var cardId in reader.GetAvailableMockCards())
                {
                    Console.WriteLine($"  - {cardId}");
                }

                // カード挿入シミュレート
                Console.WriteLine("\nCARD001を挿入中...");
                var cardInfo = await reader.SimulateCardInsertAsync("CARD001");
                
                // カード情報表示
                Console.WriteLine($"\nカード情報:");
                Console.WriteLine($"  ID: {cardInfo.CardId}");
                Console.WriteLine($"  タイプ: {cardInfo.CardType}");
                Console.WriteLine($"  発行日: {cardInfo.IssueDate:yyyy/MM/dd}");
                Console.WriteLine($"  有効期限: {cardInfo.ExpiryDate:yyyy/MM/dd}");
                Console.WriteLine($"  プロパティ:");
                foreach (var prop in cardInfo.Properties)
                {
                    Console.WriteLine($"    {prop.Key}: {prop.Value}");
                }

                // 認証テスト
                Console.WriteLine("\n認証テスト:");
                var authResult = await reader.AuthenticateAsync("1234");
                Console.WriteLine($"認証結果: {(authResult ? "成功" : "失敗")}");

                // データ書き込みテスト
                Console.WriteLine("\nデータ書き込みテスト:");
                await reader.WriteCardAsync("LastAccess", DateTime.Now.ToString());
                
                // 更新されたカード情報を読み取り
                var updatedCard = await reader.ReadCardAsync();
                Console.WriteLine($"LastAccess: {updatedCard.Properties.GetValueOrDefault("LastAccess", "なし")}");

                // カード抜き取り
                Console.WriteLine("\nカードを抜き取り中...");
                await reader.SimulateCardRemoveAsync();

                // 切断
                Console.WriteLine("\nリーダーから切断中...");
                await reader.DisconnectAsync();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"エラー: {ex.Message}");
            }

            Console.WriteLine("\nEnterキーを押して終了...");
            Console.ReadLine();
        }
    }
}
