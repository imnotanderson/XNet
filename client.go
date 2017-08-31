package XNet

import (
	"encoding/binary"
	"errors"
	"net"
	"sync"
)

/*
[RULE](LittleEndian)
C->S
first pkt
0:new conn
1+ encode(connId,token):reconn
S->C
first pkt
new conn:
connId+token
8b+0b
reconn:
0:auth ok +  token
1:auth fail
*/

var (
	ERR_CONN_DIE = errors.New("conn_die")
)

type Client struct {
	tokenEncoded []byte
	token        []byte
	connId       uint64
	die          chan struct{}
	conn         net.Conn
	recvLock     sync.RWMutex
	recvData     []byte
	sendLock     sync.RWMutex
	sendData     []byte
	recvSign     chan struct{}
	sendSign     chan struct{}
}

func newClient(connID uint64, conn net.Conn) *Client {
	token := newToken()
	c := &Client{
		connId:       connID,
		token:        token,
		die:          make(chan struct{}, 0),
		conn:         conn,
		tokenEncoded: encodeToken(connID, token),
		recvSign:     make(chan struct{}),
		sendSign:     make(chan struct{}),
	}
	return c
}

func newToken() []byte {
	return make([]byte, TOKEN_LEN)
}

func encodeToken(connId uint64, token []byte) []byte {
	data := make([]byte, CONN_ID_LEN)
	binary.LittleEndian.PutUint64(data, connId)
	return data
}

func (c *Client) start() {
	//new conn first pkt
	connIdData := make([]byte, CONN_ID_LEN)
	binary.LittleEndian.PutUint64(connIdData, c.connId)
	c.conn.Write(connIdData)
	c.conn.Write(c.token)
	go c.raw_recv(c.conn)
	go c.raw_send(c.conn)
}

func (c *Client) raw_recv(conn net.Conn) {
	payload := make([]byte, 1024)
	for {
		n, err := conn.Read(payload)
		c.writeToRecvData(payload[:n])
		if err != nil {
			return
		}
	}
}

func (c *Client) writeToRecvData(data []byte) {
	c.recvLock.Lock()
	defer c.recvLock.Unlock()
	c.recvData = append(c.recvData, data...)
	c.recvSign <- struct{}{}
}

func (c *Client) writeToSendData(data []byte) {
	if len(data) == 0 {
		return
	}
	c.sendLock.Lock()
	defer func() {
		c.sendLock.Unlock()
		c.sendSign <- struct{}{}
	}()
	c.sendData = append(c.sendData, data...)
	if len(c.sendData) > SEND_BUFFER_MAX {
		c.Close()
	}
}

func (c *Client) raw_send(conn net.Conn) {
	payload := make([]byte, 1024)
	for {
		select {
		case <-c.sendSign:
		case <-c.die:
			return
		}
		c.sendLock.Lock()
		n := copy(payload, c.sendData)
		c.sendData = c.sendData[:0]
		c.sendLock.Unlock()
		if n == 0 {
			continue
		}
		sendLen, err := conn.Write(payload[:n])
		if err != nil {
			c.sendData = append(payload[sendLen:n], c.sendData...)
			return
		}
	}
}

func (c *Client) onReconnect(conn net.Conn) error {
	token := newToken()
	_, err := conn.Write([]byte{0})
	if err != nil {
		return err
	}
	_, err = conn.Write(token)
	if err != nil {
		return err
	}
	c.token = token
	c.conn.Close()
	c.conn = conn
	c.tokenEncoded = encodeToken(c.connId, c.token)
	go c.raw_send(c.conn)
	go c.raw_recv(c.conn)
	return nil
}

func (c *Client) Close() error {
	select {
	case <-c.die:
		return ERR_CONN_DIE
	default:
		close(c.die)
		return nil
	}
}

func (c *Client) checkToken(token []byte) bool {
	if len(token) != len(c.tokenEncoded) {
		return false
	}
	for k, v := range token {
		if v != c.tokenEncoded[k] {
			return false
		}
	}
	return true
}

func (c *Client) Read(b []byte) (n int, err error) {
	n = len(b)
	for {
		if c.getRecvLen() == 0 {
			select {
			case <-c.recvSign:
			case <-c.die:
				return 0, ERR_CONN_DIE
			}
		} else {
			break
		}
	}
	c.recvLock.Lock()
	if len(c.recvData) < n {
		n = len(c.recvData)
	}
	copy(b, c.recvData[0:n])
	c.recvData = c.recvData[n:]
	c.recvLock.Unlock()
	return n, nil
}

func (c *Client) Write(b []byte) (n int, err error) {
	select {
	case <-c.die:
		return 0, ERR_CONN_DIE
	default:
		c.writeToSendData(b)
		return len(b), nil
	}
}

func (c *Client) getRecvLen() int {
	c.recvLock.RLock()
	defer c.recvLock.RUnlock()
	return len(c.recvData)
}
