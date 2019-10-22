# A10Server


電動オナホール[A10ピストンSA](https://www.vorze.jp/a10pistonsa/)をRESTAPIで制御できるWebサーバ


## 使い方

単体でも使用可能だが、下記pluginとの連携を想定して設計している

[KoikatsuA10](https://github.com/amateras977/KoikatsuA10)


### 用意するもの

- A10ピストンSA本体
  - これがなければはじまらない
- [専用無線アダプタ](https://www.e-nls.com/pict1-41903?c2=9999)
  - 要購入専用品のため一般の品では代用できない
- [VORZE PLAYER](https://vorzeinteractive.com/download)
  - 目的はセットで入っているA10ピストンSA用のドライバ。インストールが完了すれば一緒に導入される

### インストール

- 専用無線アダプタをPCに接続する
- A10ピストンSAの電源を入れる
  - 専用無線アダプタとA10ピストンSAのランプが青く点灯し続ければ認識成功
- デバイスマネージャーを開き、ポート(COMとLPT)のツリーにVorze_USB(COM4)が増えていることを確認
  - Vorze_USBが居ないならドライバ(VORZE PLAYER)の再インストールと再起動をして再挑戦
  - COM4以外のポートが指定されていたら、COM4に切り替えておくこと
- 最新版のA10Serverをダウンロードする
  - [Releases](https://github.com/amateras977/A10Server/releases)
- A10Server.exeを実行
- ブラウザからこのURLを開き、A10ピストンSAが1発動けばインストール完了
  - [テスト用URL](http://localhost:8080/api/addQueue?interval=0.3&direction=1)

## RESTAPI

/api/addQueue

ストローク操作のキュー予約を追加する

- パラメータ
  - interval
    - ストローク1回にかける目安時間(sec)
  - direction
    - ストロークの方向。1が奥、-1が手前

/api/clearQueue

ストローク操作の予約キューを消去する

## ビルド方法

.Net Core 3.0をビルドでき、dotnetコマンドを利用できる環境を用意すること。
VisualStudio 2019を推奨

このリポジトリを適当なディレクトリに展開し、リポジトリの直下でこのコマンドを実行

```
dotnet publish -r win10-x64 -c Release --self-contained
```
