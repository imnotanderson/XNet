XNet
====
在tcp之上构建的一套自动重连协议,网络中断的大部分时候,上层逻辑可以像没有断开连接一样处理逻辑.

Features
---------

* 入坑弃坑简单方便.
* 重连时传输数据尽量小.
* 完全不影响上层逻辑,就像没有发生过重连一样.
* 代码量少,阅读修改方便.
* 方便的链接事件回调

Protocol
---------

#### 新连接

+ 客户端发送 1 byte 0,表示新连接
+ 服务端收到后下发 8 byte uint64 表示connId 以及一个可自定义长度的 token
+ 之后正常通信

#### 重连

+ 客户端发送 1 byte 1, 以及 8 byte connId  表示对应connId重连
+ 客户端继续发送 由connId+token组成的 encodeToken ,对应encode算法可自定义
+ 服务端验证对应 connid 以及 encodeToken 通过 下发 1 byte 0 + 新 token 之后正常通信.
+ 如果验证失败 服务端下发 1 byte 1 后断开socket.

Links
---------

+ [SNET](https://github.com/funny/snet)
+ [在移动网络上创建更稳定的连接](http://blog.codingnow.com/2014/02/connection_reuse.html) by [云风](https://github.com/cloudwu)

Community
---------

* QQ 群：46914035
