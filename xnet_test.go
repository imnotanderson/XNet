package XNet

import (
	"encoding/binary"
	"io"
	"testing"
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
