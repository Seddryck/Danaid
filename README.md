# Danaid

![Logo](https://raw.githubusercontent.com/Seddryck/Danaid/main/assets/danaid-logo-256.png)

A lightweight, production-ready capture layer that converts RabbitMQ streams into durable, replayable datasets for lakehouse architectures.

RabbitMQ Capture bridges the gap between queue-based ingestion and file-based processing by continuously consuming messages, batching them, and persisting them to storage (Delta, Parquet, or JSON). This enables seamless integration with tools like Databricks Auto Loader and standardizes ingestion around a storage-first approach.

The project provides a clear set of configurable policies — including batching, acknowledgements, and idempotency — allowing teams to control reliability, performance, and consistency without reinventing ingestion logic.

### Key Features
- Reliable ingestion from RabbitMQ with safe acknowledgement handling
- Flexible batching strategies (time, size, count, hybrid)
- Built-in idempotency patterns for duplicate handling
- Storage-first design enabling replay and audit
- Optimized file layout for Auto Loader and downstream processing
- Policy-driven architecture for consistency and governance

### Why

RabbitMQ is a queue, not a log. Once messages are consumed, they are gone. This project introduces a capture layer that turns ephemeral streams into persistent data, making them compatible with modern data platforms.

### Outcome

All incoming data — regardless of source — becomes:

- durable
- replayable
- observable

ready for lakehouse processing

[About]: #about (About)
[Installing]: #installing (Installing)
[Quickstart]: #quickstart (Quickstart)

## About

**Social media:** [![website](https://img.shields.io/badge/website-seddryck.github.io/Danaid-fe762d.svg)](https://seddryck.github.io/Danaid)
[![twitter badge](https://img.shields.io/badge/twitter%20Danaid-@Seddryck-blue.svg?style=flat&logo=twitter)](https://twitter.com/Seddryck)

**Releases:** [![GitHub releases](https://img.shields.io/github/v/release/seddryck/danaid?label=GitHub%20releases)](https://github.com/seddryck/danaid/releases/latest) 
[![nuget](https://img.shields.io/nuget/v/Danaid-cli.svg)](https://www.nuget.org/packages/Danaid.Core/) <!-- [![Docker Image Version](https://img.shields.io/docker/v/seddryck/danaid?label=docker%20hub&color=0db7ed)](https://hub.docker.com/repository/docker/seddryck/danaid/)--> [![GitHub Release Date](https://img.shields.io/github/release-date/seddryck/Danaid.svg)](https://github.com/Seddryck/Danaid/releases/latest) [![licence badge](https://img.shields.io/badge/License-Apache%202.0-yellow.svg)](https://github.com/Seddryck/Danaid/blob/master/LICENSE) 

**Dev. activity:** [![GitHub last commit](https://img.shields.io/github/last-commit/Seddryck/Danaid.svg)](https://github.com/Seddryck/Danaid/commits)
![Still maintained](https://img.shields.io/maintenance/yes/2026.svg)
![GitHub commit activity](https://img.shields.io/github/commit-activity/y/Seddryck/Danaid)

**Continuous integration builds:** [![Build status](https://ci.appveyor.com/api/projects/status/infwf1wchegmda9u?svg=true)](https://ci.appveyor.com/project/Seddryck/Danaid/)
[![Tests](https://img.shields.io/appveyor/tests/seddryck/Danaid.svg)](https://ci.appveyor.com/project/Seddryck/Danaid/build/tests)
[![CodeFactor](https://www.codefactor.io/repository/github/seddryck/Danaid/badge)](https://www.codefactor.io/repository/github/seddryck/Danaid)
[![codecov](https://codecov.io/github/Seddryck/Danaid/branch/main/graph/badge.svg?token=YRA8IRIJYV)](https://codecov.io/github/Seddryck/Danaid)
<!-- [![FOSSA Status](https://app.fossa.com/api/projects/git%2Bgithub.com%2FSeddryck%2FDanaid.svg?type=shield)](https://app.fossa.com/projects/git%2Bgithub.com%2FSeddryck%2FDanaid?ref=badge_shield) -->

**Status:** [![stars badge](https://img.shields.io/github/stars/Seddryck/Danaid.svg)](https://github.com/Seddryck/Danaid/stargazers)
[![Bugs badge](https://img.shields.io/github/issues/Seddryck/Danaid/bug.svg?color=red&label=Bugs)](https://github.com/Seddryck/Danaid/issues?utf8=%E2%9C%93&q=is:issue+is:open+label:bug+)
[![Top language](https://img.shields.io/github/languages/top/seddryck/Danaid.svg)](https://github.com/Seddryck/Danaid/search?l=C%23)

## Installing
TBC

## Quickstart
