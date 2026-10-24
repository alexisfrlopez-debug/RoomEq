# RoomEq ASIO bridge core — tests portables (aucune dépendance Windows)
CC      ?= cc
CFLAGS  ?= -std=c11 -Wall -Wextra -Werror -O2 -I.

test: test_bridge_core
	./test_bridge_core

test_bridge_core: test_bridge_core.c roomeq_asio_bridge.c roomeq_asio_protocol.h
	$(CC) $(CFLAGS) -D_GNU_SOURCE=1 -o $@ test_bridge_core.c roomeq_asio_bridge.c

clean:
	rm -f test_bridge_core

.PHONY: test clean
