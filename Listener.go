package XNet

import (
	"encoding/binary"
	"io"
	"net"
	"sync"
	"sync/atomic"
)

const (
	CONN_ID_LEN int = 8
	TOKEN_LEN       = 4
)

type Listener struct {
	lsn           net.Listener
	die           chan struct{}
	clientMapLock sync.RWMutex
	clientMap     map[uint64]*Client
	atomicConnID  uint64
	chClient      chan *Client
}

func NewListener(addr string) (*Listener, error) {
	l, err := net.Listen("tcp", addr)
	if err != nil {
		return nil, err
	}

	lsn := &Listener{
		lsn:       l,
		die:       make(chan struct{}, 0),
		chClient:  make(chan *Client, 1024),
		clientMap: make(map[uint64]*Client),
	}
	lsn.run()
	return lsn, nil
}

func (l *Listener) run() {
	go func() {
		for {
			conn, err := l.lsn.Accept()
			if err != nil {
				continue
			}
			go l.handleConn(conn)
		}
	}()
}

func (l *Listener) Accept() *Client {
	c := <-l.chClient
	l.putClient(c)
	go c.start()
	go func() {
		<-c.die
		l.removeClient(c)
	}()
	return c
}

func (l *Listener) handleConn(conn net.Conn) {
	connFlag := []byte{0}
	conn.Read(connFlag)
	switch connFlag[0] {
	case 0:
		//0:new conn
		{
			connID := atomic.AddUint64(&l.atomicConnID, 1)
			client := newClient(connID, conn)
			select {
			case l.chClient <- client:
			case <-l.die:
			}
		}
	case 1:
		//1+ connId+encode(connId,token):reconn
		{
			connIdBytes := make([]byte, CONN_ID_LEN)
			_, err := conn.Read(connIdBytes)
			if err != nil {
				return
			}
			connId := binary.LittleEndian.Uint64(connIdBytes)
			client := l.getClient(connId)
			if client == nil {
				conn.Write([]byte{1})
				return
			}
			token := make([]byte, len(client.tokenEncoded))
			_, err = io.ReadFull(conn, token)
			if err != nil {
				return
			}
			if client.checkToken(token) == false {
				conn.Write([]byte{1})
				return
			}

			if err = client.onReconnect(conn); err != nil {
				return
			}
			l.putClient(client)
		}
	}
}

func (l *Listener) putClient(c *Client) {
	l.clientMapLock.Lock()
	defer l.clientMapLock.Unlock()
	l.clientMap[c.connId] = c
}

func (l *Listener) removeClient(c *Client) {
	l.clientMapLock.Lock()
	defer l.clientMapLock.Unlock()
	if c == l.clientMap[c.connId] {
		delete(l.clientMap, c.connId)
	}
}

func (l *Listener) getClient(connId uint64) *Client {
	l.clientMapLock.RLock()
	defer l.clientMapLock.RUnlock()
	return l.clientMap[connId]
}
