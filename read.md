{
  "project": "W101 Kelime Oyunu Backend API",
  "general_rules": [
    "Tüm uç noktalar RESTful ve JSON formatında veri döner.",
    "Kullanıcı kimlik doğrulaması JWT ile yapılır.",
    "Tüm kritik işlemlerde (kayıt, login, diamond, maç sonucu) transaction kullanılmalı.",
    "Veritabanındaki ilişkiler korunur, örneğin user_id, rank_id, chat_id vs."
  ],
  "cache_strategy": {
    "words": {
      "description": "Kelime listesi her oyun başında .txt olarak cihaz cache'ine alınır. Oyun devam ettiği sürece api'ye tekrar istek yapılmaz.",
      "endpoint": "/api/words/download",
      "method": "GET",
      "params": { "lang": "tr" },
      "output_type": "text/plain (kelime başına bir satır, ör: 'elma\\nkedi\\nmasa...')",
      "rules": [
        "Kelime listesi güncellendiğinde yeni bir versiyon numarası ile birlikte döner.",
        "Cihaz cache'deki versiyon eski ise API yeni versiyon numarasını döner, cihaz yeni listeyi çeker.",
        "Aynı kelime listesi birden fazla oyunda tekrar tekrar kullanılabilir.",
        "Backend’de kelimeler ayrı bir table veya storage’da versiyonlanır."
      ]
    }
  },
  "apis": [
    {
      "name": "Kullanıcı Kayıt",
      "endpoint": "/api/auth/register",
      "method": "POST",
      "input": {
        "username": "string (unique, min 3, max 20 karakter)",
        "email": "string (unique, email format)",
        "password": "string (min 6 karakter)"
      },
      "output": {
        "user_id": "int",
        "username": "string",
        "email": "string",
        "token": "string",
        "created_at": "timestamp"
      },
      "rules": [
        "Username ve email benzersiz olmalı.",
        "Şifre hash'lenerek saklanmalı.",
        "Kullanıcı oluşturulunca users tablosuna kaydedilir, level=1, diamonds=0, wins=0, losses=0, created_at now.",
        "Başarılı kayıt sonrası JWT token döner."
      ]
    },
    {
      "name": "Kullanıcı Giriş",
      "endpoint": "/api/auth/login",
      "method": "POST",
      "input": {
        "username": "string",
        "password": "string"
      },
      "output": {
        "user_id": "int",
        "username": "string",
        "email": "string",
        "token": "string"
      },
      "rules": [
        "Şifre hash'i ile karşılaştırma yapılır.",
        "Başarılı login sonrası JWT token döner.",
        "Son giriş zamanı users.last_login alanına yazılır."
      ]
    },
    {
      "name": "Profil Bilgisi",
      "endpoint": "/api/profile/me",
      "method": "GET",
      "auth": true,
      "output": {
        "user_id": "int",
        "username": "string",
        "display_name": "string",
        "email": "string",
        "avatar_url": "string",
        "rank_id": "int",
        "rank_name": "string (ranks.name)",
        "diamonds": "int",
        "level": "int",
        "wins": "int",
        "losses": "int",
        "win_rate": "decimal",
        "last_login": "timestamp",
        "created_at": "timestamp",
        "updated_at": "timestamp"
      },
      "rules": [
        "Win rate = wins / (wins + losses)",
        "Rank bilgisi join ile eklenir."
      ]
    },
    {
      "name": "Profil Güncelle",
      "endpoint": "/api/profile/update",
      "method": "PUT",
      "auth": true,
      "input": {
        "display_name": "string",
        "avatar_url": "string",
        "email": "string (opsiyonel, unique)",
        "password": "string (opsiyonel, min 6 karakter)"
      },
      "output": {
        "success": "bool",
        "message": "string"
      },
      "rules": [
        "Sadece güncellenmek istenen alanlar değiştirilebilir.",
        "Email veya şifre güncellendiğinde benzersizlik ve minimum kriterler tekrar sağlanmalı.",
        "password varsa hash’lenir."
      ]
    },
    {
      "name": "Kullanıcı Ayarları",
      "endpoint": "/api/user-settings",
      "method": "GET/PUT",
      "auth": true,
      "input/PUT": {
        "sound_enabled": "bool",
        "music_enabled": "bool",
        "notification_enabled": "bool",
        "language": "string"
      },
      "output": {
        "sound_enabled": "bool",
        "music_enabled": "bool",
        "notification_enabled": "bool",
        "language": "string"
      },
      "rules": [
        "GET ile kullanıcının ayarları çekilir.",
        "PUT ile kullanıcının ayarları güncellenir."
      ]
    },
    {
      "name": "Dashboard & Son Oyunlar",
      "endpoint": "/api/dashboard/summary",
      "method": "GET",
      "auth": true,
      "output": {
        "total_games": "int (users.wins + users.losses)",
        "total_wins": "int",
        "series": "int (üst üste kazanılan maç sayısı)",
        "diamonds": "int"
      }
    },
    {
      "name": "Son Oyunlar Listesi",
      "endpoint": "/api/matches/recent",
      "method": "GET",
      "auth": true,
      "output": [
        {
          "match_type": "string (Tek Oyun, Hızlı Oyun, Lobi Oyunu)",
          "difficulty": "string (Kolay, Orta, Zor, Ekstrem)",
          "score": "int",
          "result": "string (win/lose)",
          "created_at": "timestamp",
          "time_ago": "string"
        }
      ]
    },
    {
      "name": "Lobi Listesi",
      "endpoint": "/api/lobbies",
      "method": "GET",
      "output": [
        {
          "id": "int",
          "name": "string",
          "min_level": "int",
          "min_diamonds": "int",
          "current_players": "int",
          "max_players": "int",
          "is_full": "bool"
        }
      ],
      "rules": [
        "Kullanıcı mevcut elmas ve level ile girebileceği lobileri görebilir.",
        "Her lobiye max oyuncu limiti vardır.",
        "is_full true ise katılım engellenir."
      ]
    },
    {
      "name": "Lobiye Katılım",
      "endpoint": "/api/lobbies/join",
      "method": "POST",
      "auth": true,
      "input": { "lobby_id": "int" },
      "output": { "can_join": "bool", "reason": "string" },
      "rules": [
        "Kullanıcı lobi için yeterli elmasa ve seviyeye sahip olmalı.",
        "Lobi doluysa giriş reddedilir."
      ]
    },
    {
      "name": "Lobi Masalarını Listele",
      "endpoint": "/api/lobbies/{lobby_id}/tables",
      "method": "GET",
      "output": [
        {
          "id": "int",
          "name": "string",
          "bet": "int",
          "current_players": "int",
          "max_players": "int",
          "status": "string (boşta, açık, dolu)",
          "players": [
            {
              "user_id": "int",
              "display_name": "string",
              "avatar_url": "string"
            }
          ]
        }
      ]
    },
    {
      "name": "Masa Oluştur",
      "endpoint": "/api/tables/create",
      "method": "POST",
      "auth": true,
      "input": {
        "lobby_id": "int",
        "name": "string",
        "bet": "int",
        "max_players": "int"
      },
      "output": {
        "table_id": "int",
        "success": "bool"
      },
      "rules": [
        "Masa aynı lobide mevcut isimle oluşturulamaz.",
        "Başlatan kullanıcı otomatik masaya eklenir."
      ]
    },
    {
      "name": "Masaya Katıl",
      "endpoint": "/api/tables/join",
      "method": "POST",
      "auth": true,
      "input": { "table_id": "int" },
      "output": { "success": "bool", "seat_number": "int", "reason": "string" },
      "rules": [
        "Masa doluysa giriş engellenir.",
        "Kullanıcı yeterli elmasa sahip olmalı, aksi halde giriş engellenir.",
        "Kullanıcı masaya eklendiğinde bet kadar elmas düşülür."
      ]
    },
    {
      "name": "Oyun Başlat",
      "endpoint": "/api/matches/start",
      "method": "POST",
      "auth": true,
      "input": { "table_id": "int" },
      "output": { "match_id": "int", "success": "bool" },
      "rules": [
        "Masada minimum 2, maksimum 4 oyuncu varsa oyun başlar.",
        "Her oyuncuya rastgele taşlar dağıtılır.",
        "Oyun tipi (tekli/online/lobi) inputta otomatik belirlenir."
      ]
    },
    {
      "name": "Oyun Bitir",
      "endpoint": "/api/matches/finish",
      "method": "POST",
      "auth": true,
      "input": {
        "match_id": "int",
        "winner_user_id": "int",
        "scores": [
          {
            "user_id": "int",
            "score": "int",
            "is_winner": "bool"
          }
        ]
      },
      "output": { "success": "bool" },
      "rules": [
        "Kazanan kullanıcıya masadaki toplam bet elmas (oyuncu sayısı x bet) verilir.",
        "Kaybeden oyunculara elmas iadesi yoktur.",
        "users tablosunda wins, losses, diamonds güncellenir.",
        "Kazanma serisi users tablosunda tutulur (win streak)."
      ]
    },
    {
      "name": "Maç Geçmişi",
      "endpoint": "/api/matches/history",
      "method": "GET",
      "auth": true,
      "output": [
        {
          "match_id": "int",
          "type": "string",
          "bet": "int",
          "score": "int",
          "result": "win/lose",
          "date": "timestamp"
        }
      ]
    },
    {
      "name": "Elmas Mağazası Listesi",
      "endpoint": "/api/store/items",
      "method": "GET",
      "output": [
        {
          "shop_item_id": "int",
          "name": "string",
          "diamond_amount": "int",
          "price_local": "decimal",
          "currency": "string"
        }
      ]
    },
    {
      "name": "Elmas Satın Al",
      "endpoint": "/api/store/buy",
      "method": "POST",
      "auth": true,
      "input": {
        "shop_item_id": "int"
      },
      "output": {
        "success": "bool",
        "diamonds": "int",
        "transaction_id": "int"
      },
      "rules": [
        "Satın alma başarılıysa purchases ve diamond_transactions tablolarına kayıt eklenir.",
        "Kullanıcıya diamonds alanı kadar elmas eklenir.",
        "payment_status 'paid' olmalı, aksi halde diamonds verilmez."
      ]
    },
    {
      "name": "Elmas İşlemleri",
      "endpoint": "/api/diamonds/history",
      "method": "GET",
      "auth": true,
      "output": [
        {
          "id": "int",
          "type": "string (purchase, win, lose, reward, ad)",
          "amount": "int",
          "description": "string",
          "created_at": "timestamp"
        }
      ]
    },
    {
      "name": "Günlük Giriş Ödülü",
      "endpoint": "/api/rewards/daily",
      "method": "POST",
      "auth": true,
      "output": { "success": "bool", "reward": "int" },
      "rules": [
        "Kullanıcıya bir gün içinde yalnızca bir kez giriş ödülü verilir.",
        "Ödül daily_logins tablosuna kayıt edilir."
      ]
    },
    {
      "name": "Reklam Ödülü",
      "endpoint": "/api/rewards/ad",
      "method": "POST",
      "auth": true,
      "output": { "success": "bool", "reward": "int" },
      "rules": [
        "Kullanıcı 20 dakika arayla reklam izleyebilir.",
        "ad_rewards tablosuna kayıt edilir.",
        "Süre dolmadan tekrar talep edilirse hata döner."
      ]
    },
    {
      "name": "Arkadaş Ara",
      "endpoint": "/api/friends/search",
      "method": "GET",
      "auth": true,
      "params": { "query": "string" },
      "output": [
        {
          "user_id": "int",
          "username": "string",
          "display_name": "string",
          "avatar_url": "string",
          "rank_name": "string"
        }
      ]
    },
    {
      "name": "Arkadaşlık İsteği Gönder",
      "endpoint": "/api/friends/request",
      "method": "POST",
      "auth": true,
      "input": { "target_user_id": "int" },
      "output": { "success": "bool" },
      "rules": [
        "Daha önce gönderilmiş ve bekleyen istek varsa yeni istek oluşturulamaz.",
        "friend_requests tablosuna kayıt edilir."
      ]
    },
    {
      "name": "Arkadaşlık İstekleri",
      "endpoint": "/api/friends/requests",
      "method": "GET",
      "auth": true,
      "output": [
        {
          "request_id": "int",
          "from_user_id": "int",
          "to_user_id": "int",
          "status": "pending/accepted/rejected",
          "created_at": "timestamp"
        }
      ]
    },
    {
      "name": "Arkadaş Listesi",
      "endpoint": "/api/friends/list",
      "method": "GET",
      "auth": true,
      "output": [
        {
          "user_id": "int",
          "display_name": "string",
          "avatar_url": "string",
          "rank_name": "string",
          "status": "accepted"
        }
      ]
    },
    {
      "name": "Arkadaşa Mesaj Gönder",
      "endpoint": "/api/messages/send",
      "method": "POST",
      "auth": true,
      "input": { "receiver_user_id": "int", "content": "string" },
      "output": { "success": "bool", "message_id": "int" },
      "rules": [
        "Mesaj messages tablosuna kaydedilir.",
        "Küfür, spam gibi mesajlar filtrelenir."
      ]
    },
    {
      "name": "Arkadaş Mesaj Geçmişi",
      "endpoint": "/api/messages/history",
      "method": "GET",
      "auth": true,
      "params": { "friend_id": "int" },
      "output": [
        {
          "sender_user_id": "int",
          "receiver_user_id": "int",
          "content": "string",
          "created_at": "timestamp"
        }
      ]
    },
    {
      "name": "Oyun İçi Sohbet Mesajı Gönder",
      "endpoint": "/api/match-chat/send",
      "method": "POST",
      "auth": true,
      "input": { "chat_id": "int", "content": "string" },
      "output": { "success": "bool", "message_id": "int" },
      "rules": [
        "Mesaj chat_messages tablosuna kaydedilir.",
        "Küfür/spam engellenir."
      ]
    },
    {
      "name": "Oyun İçi Sohbet Mesajları",
      "endpoint": "/api/match-chat/history",
      "method": "GET",
      "auth": true,
      "params": { "chat_id": "int" },
      "output": [
        {
          "sender_user_id": "int",
          "content": "string",
          "created_at": "timestamp"
        }
      ]
    }
  ]
}