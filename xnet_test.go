package XNet

import (
	"encoding/binary"
	"fmt"
	"io"
	"testing"
	"time"
)

func TestXNet(t *testing.T) {
	lsn, err := NewListener(":34560")
	if err != nil {
		panic(err)
	}
	println("lsn...")
	for {
		c := lsn.Accept()
		println("accept...")
		go sendTest(c)
		go func() {
			for {
				//data := make([]byte, 1024)
				lenData := make([]byte, 4)
				n, err := io.ReadFull(c, lenData)
				//:= c.Read(lenData)
				if err != nil {
					panic(err)
				}
				println(n)
				len := binary.LittleEndian.Uint32(lenData)
				strData := make([]byte, int(len))
				_, err = io.ReadFull(c, strData)
				if err != nil {
					panic(err)
				}
				str := string(strData)
				println("recv:", str)
			}
		}()
	}

}

var sendCount int = 0

func sendTest(c *Client) {
	for {
		str := fmt.Sprintf("send from svr %v", sendCount)
		sendCount++
		strData := []byte(str)
		len := len(strData)
		lenData := make([]byte, 4)
		binary.LittleEndian.PutUint32(lenData, uint32(len))
		c.Write(lenData)
		c.Write(strData)
		time.After(time.Second)
	}
}
