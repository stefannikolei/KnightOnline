#include "stdafx.h"
#include "logger.h"
#include "Ini.h"

#include <spdlog/spdlog.h>
#include <spdlog/sinks/daily_file_sink.h>
#include <spdlog/common.h>
#include <spdlog/sinks/stdout_color_sinks.h>
#include <spdlog/async.h>
#include <spdlog/async_logger.h>

namespace logger
{
	void SetupExtraLogger(CIni& ini,
		const std::string& appName, const std::string& logFileConfigProp,
		const std::string& exePath,
		std::shared_ptr<spdlog::details::thread_pool> threadPool);
}

/// \brief Sets up spdlog from an ini file using standardized server settings
/// \param ini server application's ini file (already loaded)
/// \param appName application name (VersionManager, Aujard, AIServer, Ebenezer)
/// \param baseDir base directory to store logs folder under. must end in a trailing slash.
void logger::SetupLogger(CIni& ini, const std::string& appName, const std::string& baseDir)
{
	// setup file logger
	std::string defaultLogPath = std::format("logs/{}.log", appName);
	std::string fileName = ini.GetString(ini::LOGGER, ini::FILE, defaultLogPath);
	auto fileLogger = std::make_shared<spdlog::sinks::daily_file_format_sink_mt>(baseDir + fileName, 0, 0);

	// setup console logger
	auto consoleLogger = std::make_shared<spdlog::sinks::stdout_color_sink_mt>();

	// setup multi-sink async logger as default (combines file+console logger)
	spdlog::init_thread_pool(MessageQueueSize, ThreadPoolSize);
	std::vector<spdlog::sink_ptr> sinks{fileLogger, consoleLogger};
	auto threadPool = spdlog::thread_pool();
	auto appLogger = std::make_shared<spdlog::async_logger>(
		appName, sinks.begin(), sinks.end(), threadPool, spdlog::async_overflow_policy::block);
	spdlog::set_default_logger(appLogger);

	// add any extra loggers
	if (appName == AIServer)
	{
		SetupExtraLogger(ini, AIServerItem, ini::ITEM_LOG_FILE, baseDir, threadPool);
		SetupExtraLogger(ini, AIServerUser, ini::USER_LOG_FILE, baseDir, threadPool);
	}
	else if (appName == Ebenezer)
	{
		SetupExtraLogger(ini, EbenezerEvent, ini::EVENT_LOG_FILE, baseDir, threadPool);
		SetupExtraLogger(ini, EbenezerRegion, ini::REGION_LOG_FILE, baseDir, threadPool);
	}

	// set default logger level and pattern
	// we default to debug level logging
	int logLevel = ini.GetInt(ini::LOGGER, ini::LEVEL, spdlog::level::debug);
	spdlog::set_level(static_cast<spdlog::level::level_enum>(logLevel));

	std::string logPattern = ini.GetString(ini::LOGGER, ini::PATTERN, ini::DEFAULT_LOG_PATTERN);
	spdlog::set_pattern(logPattern);

	// periodically flush all *registered* loggers every 3 seconds:
	// warning: only use if all your loggers are thread-safe ("_mt" loggers)
	spdlog::flush_every(std::chrono::seconds(3));

	spdlog::info("{} logger configured", appName);
}

void logger::SetupExtraLogger(CIni& ini,
	const std::string& appName, const std::string& logFileConfigProp,
	const std::string& baseDir,
	std::shared_ptr<spdlog::details::thread_pool> threadPool)
{
	// setup file logger
	std::string defaultLogPath = std::format("logs/{}.log", appName);
	std::string fileName = ini.GetString(ini::LOGGER, logFileConfigProp, defaultLogPath);
	auto fileLogger = std::make_shared<spdlog::sinks::daily_file_format_sink_mt>(baseDir + fileName, 0, 0);

	// setup console logger
	auto consoleLogger = std::make_shared<spdlog::sinks::stdout_color_sink_mt>();

	// setup multi-sink async logger as default (combines file+console logger)
	std::vector<spdlog::sink_ptr> sinks{fileLogger, consoleLogger};
	auto extraLogger = std::make_shared<spdlog::async_logger>(
		appName, sinks.begin(), sinks.end(), threadPool, spdlog::async_overflow_policy::block);

	// set default logger level and pattern
	// we default to debug level logging
	int logLevel = ini.GetInt(ini::LOGGER, ini::LEVEL, spdlog::level::debug);
	extraLogger->set_level(static_cast<spdlog::level::level_enum>(logLevel));

	std::string logPattern = ini.GetString(ini::LOGGER, ini::PATTERN, ini::DEFAULT_LOG_PATTERN);
	extraLogger->set_pattern(logPattern);

	// register the logger
	spdlog::register_logger(extraLogger);
	spdlog::get(appName)->info("{} logger configured", appName);
}
