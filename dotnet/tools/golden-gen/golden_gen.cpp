// Golden-vector generator for the C# port.
//
// Runs the ORIGINAL C++ implementations from shared/ (JvCryption, crc32, lzf, djb2)
// against fixed inputs and dumps the results as JSON into dotnet/tests/vectors/.
// The vectors are checked in; the .NET tests assert byte-equality against them, which
// pins the C# port to the C++ behavior without needing a C++ toolchain in .NET CI.
//
// Build & run (from the repo root):
//   g++ -std=c++23 -O2 -I . -I shared -I deps/djb2 \
//       dotnet/tools/golden-gen/golden_gen.cpp shared/JvCryption.cpp shared/crc32.cpp shared/lzf.cpp \
//       -o /tmp/golden-gen && /tmp/golden-gen dotnet/tests/vectors
//
// NOTE on LZF determinism: lzf_compress leaves its 512 KiB hash table uninitialized
// on the stack (INIT_HTAB=0). To get deterministic output each compression runs on a
// brand-new thread whose stack pages are freshly mapped (zero-filled) — equivalent to
// the zero-initialized table the C# port uses.

#include <cstdint>
#include <cstdio>
#include <cstring>
#include <string>
#include <thread>
#include <vector>

#include "JvCryption.h"
#include "crc32.h"
#include "lzf.h"
#include "djb2_hasher.h"

// JvCryption.cpp references RandUInt64 (from globals.cpp) in GenerateKey; we never
// call it, but the linker needs the symbol.
uint64_t RandUInt64()
{
	return 0xDCE04F8975278163ULL;
}

static std::string hex(const uint8_t* data, size_t len)
{
	static const char* digits = "0123456789abcdef";
	std::string out;
	out.reserve(len * 2);
	for (size_t i = 0; i < len; i++)
	{
		out.push_back(digits[data[i] >> 4]);
		out.push_back(digits[data[i] & 0xF]);
	}
	return out;
}

static std::string hex(const std::vector<uint8_t>& data)
{
	return hex(data.data(), data.size());
}

// Deterministic byte patterns (documented so the C# tests can regenerate the inputs).
static std::vector<uint8_t> patternBytes(size_t len)
{
	std::vector<uint8_t> v(len);
	for (size_t i = 0; i < len; i++)
		v[i] = (uint8_t) ((i * 7 + 13) & 0xFF);
	return v;
}

static std::vector<uint8_t> lcgBytes(size_t len, uint32_t seed)
{
	std::vector<uint8_t> v(len);
	uint32_t s = seed;
	for (size_t i = 0; i < len; i++)
	{
		s = s * 1664525u + 1013904223u;
		v[i] = (uint8_t) (s >> 24);
	}
	return v;
}

static std::vector<uint8_t> repeatText(const char* text, size_t targetLen)
{
	std::vector<uint8_t> v;
	size_t textLen = strlen(text);
	while (v.size() < targetLen)
	{
		size_t take = std::min(textLen, targetLen - v.size());
		v.insert(v.end(), (const uint8_t*) text, (const uint8_t*) text + take);
	}
	return v;
}

int main(int argc, char** argv)
{
	if (argc < 2)
	{
		fprintf(stderr, "usage: golden-gen <output-dir>\n");
		return 1;
	}

	std::string outDir = argv[1];

	// ---------------------------------------------------------------- JvCryption
	{
		FILE* f = fopen((outDir + "/jvcryption.json").c_str(), "w");
		fprintf(f, "[\n");

		const uint64_t keys[] = { 0xDCE04F8975278163ULL, 0x0123456789ABCDEFULL };
		const size_t lens[] = { 0, 1, 7, 8, 9, 16, 64, 512 };
		bool first = true;

		for (uint64_t key : keys)
		{
			for (size_t len : lens)
			{
				std::vector<uint8_t> in = patternBytes(len);
				std::vector<uint8_t> out(len);

				CJvCryption crypt;
				crypt.SetPublicKey(key);
				crypt.Init();
				crypt.JvEncryptionFast((int) len, in.data(), out.data());

				fprintf(f, "%s  {\"key\": \"%016llx\", \"input\": \"%s\", \"output\": \"%s\"}",
					first ? "" : ",\n",
					(unsigned long long) key, hex(in).c_str(), hex(out).c_str());
				first = false;
			}
		}

		fprintf(f, "\n]\n");
		fclose(f);
	}

	// --------------------------------------------------- JvDecryptionWithCRC32
	{
		FILE* f = fopen((outDir + "/jvcryption_crc.json").c_str(), "w");
		fprintf(f, "[\n");

		const uint64_t key = 0xDCE04F8975278163ULL;
		const size_t lens[] = { 1, 8, 100, 500 };
		bool first = true;

		for (size_t len : lens)
		{
			for (int corrupt = 0; corrupt <= 1; corrupt++)
			{
				// Build payload + trailing CRC32 (seeded -1, LE), then encrypt: that is
				// exactly what a client sends on the wire.
				std::vector<uint8_t> plain = patternBytes(len);
				uint32_t crc = crc32(plain.data(), (unsigned int) plain.size(), (unsigned int) -1);
				plain.push_back((uint8_t) (crc));
				plain.push_back((uint8_t) (crc >> 8));
				plain.push_back((uint8_t) (crc >> 16));
				plain.push_back((uint8_t) (crc >> 24));

				CJvCryption crypt;
				crypt.SetPublicKey(key);
				crypt.Init();

				std::vector<uint8_t> wire(plain.size());
				crypt.JvEncryptionFast((int) plain.size(), plain.data(), wire.data());

				if (corrupt)
					wire[0] ^= 0x5A;

				std::vector<uint8_t> decrypted(wire.size());
				int result = crypt.JvDecryptionWithCRC32((int) wire.size(), wire.data(), decrypted.data());

				fprintf(f, "%s  {\"key\": \"%016llx\", \"wire\": \"%s\", \"result\": %d, \"payload\": \"%s\"}",
					first ? "" : ",\n",
					(unsigned long long) key, hex(wire).c_str(), result,
					result >= 0 ? hex(decrypted.data(), (size_t) result).c_str() : "");
				first = false;
			}
		}

		fprintf(f, "\n]\n");
		fclose(f);
	}

	// ------------------------------------------------------------------- crc32
	{
		FILE* f = fopen((outDir + "/crc32.json").c_str(), "w");
		fprintf(f, "[\n");

		struct Case { std::vector<uint8_t> data; uint32_t start; };
		std::vector<Case> cases;
		cases.push_back({ {}, 0 });
		cases.push_back({ {}, 0xFFFFFFFFu });
		std::vector<uint8_t> digits((const uint8_t*) "123456789", (const uint8_t*) "123456789" + 9);
		cases.push_back({ digits, 0 });
		cases.push_back({ digits, 0xFFFFFFFFu });
		cases.push_back({ patternBytes(256), 0 });
		cases.push_back({ patternBytes(256), 0xFFFFFFFFu });
		cases.push_back({ lcgBytes(1000, 42), 0xFFFFFFFFu });

		bool first = true;
		for (const Case& c : cases)
		{
			uint32_t result = crc32(c.data.data(), (unsigned int) c.data.size(), c.start);
			fprintf(f, "%s  {\"input\": \"%s\", \"start\": %u, \"result\": %u}",
				first ? "" : ",\n", hex(c.data).c_str(), c.start, result);
			first = false;
		}

		fprintf(f, "\n]\n");
		fclose(f);
	}

	// --------------------------------------------------------------------- lzf
	{
		FILE* f = fopen((outDir + "/lzf.json").c_str(), "w");
		fprintf(f, "[\n");

		struct Case { const char* name; std::vector<uint8_t> data; size_t outLen; };
		std::vector<Case> cases;
		cases.push_back({ "text", repeatText("the quick brown fox jumps over the lazy dog. ", 200), 0 });
		cases.push_back({ "binary-repetitive", patternBytes(1024), 0 });
		cases.push_back({ "zeros", std::vector<uint8_t>(4096, 0), 0 });
		cases.push_back({ "incompressible", lcgBytes(512, 7), 0 });
		cases.push_back({ "short", { 'a', 'b', 'c', 'd', 'e' }, 0 });
		cases.push_back({ "tight-buffer", lcgBytes(256, 99), 256 }); // expect 0 (doesn't fit)
		// Region-data-sized payload, like WIZ_COMPRESS_PACKET / AG_COMPRESSED use it.
		cases.push_back({ "large-mixed", [] {
			std::vector<uint8_t> v = repeatText("KnightOnline region payload ", 3000);
			std::vector<uint8_t> noise = lcgBytes(1000, 1234);
			v.insert(v.end(), noise.begin(), noise.end());
			return v;
		}(), 0 });

		bool first = true;
		for (const Case& c : cases)
		{
			size_t outLen = c.outLen != 0 ? c.outLen : c.data.size() * 2 + 64;
			std::vector<uint8_t> out(outLen);
			unsigned int compressedLen = 0;

			// Fresh thread => fresh zero-filled stack for the uninitialized htab.
			std::thread worker([&] {
				compressedLen = lzf_compress(c.data.data(), (unsigned int) c.data.size(), out.data(), (unsigned int) outLen);
			});
			worker.join();

			fprintf(f, "%s  {\"name\": \"%s\", \"input\": \"%s\", \"outLen\": %zu, \"compressed\": \"%s\"}",
				first ? "" : ",\n",
				c.name, hex(c.data).c_str(), outLen,
				hex(out.data(), compressedLen).c_str());
			first = false;

			// Sanity: decompression must round-trip.
			if (compressedLen > 0)
			{
				std::vector<uint8_t> back(c.data.size());
				unsigned int backLen = lzf_decompress(out.data(), compressedLen, back.data(), (unsigned int) back.size());
				if (backLen != c.data.size() || memcmp(back.data(), c.data.data(), backLen) != 0)
				{
					fprintf(stderr, "FATAL: lzf round-trip failed for %s\n", c.name);
					return 1;
				}
			}
		}

		fprintf(f, "\n]\n");
		fclose(f);
	}

	// -------------------------------------------------------------------- djb2
	{
		FILE* f = fopen((outDir + "/djb2.json").c_str(), "w");
		fprintf(f, "[\n");

		const char* strings[] = {
			"", "a", "GIVE_ITEM", "ROB_ITEM", "SELECT_MSG", "RUN_EVENT",
			"SAY", "OPEN_DOOR", "LOG_COUPON_EVENT", "ZONE_CHANGE",
			"The quick brown fox", "knight online 1298"
		};

		bool first = true;
		for (const char* s : strings)
		{
			unsigned long long h = (unsigned long long) hashing::djb2::hash(s);
			fprintf(f, "%s  {\"input\": \"%s\", \"hash\": \"%llu\"}", first ? "" : ",\n", s, h);
			first = false;
		}

		fprintf(f, "\n]\n");
		fclose(f);
	}

	printf("golden vectors written to %s\n", outDir.c_str());
	return 0;
}
