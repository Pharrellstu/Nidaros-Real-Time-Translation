/*
 * This code and all components (c) Copyright 2006 - 2025, Wowza Media Systems, LLC.  All rights reserved.
 * This code is licensed pursuant to the Wowza Public License version 1.0, available at www.wowza.com/legal.
 */

package com.wowza.wms.plugin.captions.stream;

import com.wowza.wms.stream.IMediaStream;
import com.wowza.wms.stream.IMediaStreamActionNotify;
import org.slf4j.Logger;
import org.slf4j.LoggerFactory;
import org.slf4j.MDC;

public interface StreamActionNotify extends IMediaStreamActionNotify
{
	String MDC_SERVICE_NAME = "serviceName";
	String MDC_SESSION_ID = "sessionId";
	String SERVICE_NAME_VALUE = "wowza-captions";

	Logger logger = LoggerFactory.getLogger(StreamActionNotify.class);

	@Override
	default void onPlay(IMediaStream stream, String streamName, double playStart, double playLen, int playReset) {}

	@Override
	default void onPublish(IMediaStream stream, String streamName, boolean isRecord, boolean isAppend)
	{
		String resolvedStreamName = (streamName != null && !streamName.isBlank())
				? streamName
				: (stream != null ? stream.getName() : "unknown");

		try
		{
			MDC.put(MDC_SERVICE_NAME, SERVICE_NAME_VALUE);
			MDC.put(MDC_SESSION_ID, resolvedStreamName);

			logger.info("pipeline event=stream_start streamName={} isRecord={} isAppend={}",
					resolvedStreamName, isRecord, isAppend);
		}
		finally
		{
			// IMPORTANT: don't clear sessionId here if other callbacks in the same thread should keep it.
			// Most Wowza callbacks are executed on different threads, so we keep it scoped to this call.
			MDC.remove(MDC_SESSION_ID);
			MDC.remove(MDC_SERVICE_NAME);
		}
	}

	@Override
	default void onUnPublish(IMediaStream stream, String streamName, boolean isRecord, boolean isAppend)
	{
		String resolvedStreamName = (streamName != null && !streamName.isBlank())
				? streamName
				: (stream != null ? stream.getName() : "unknown");

		try
		{
			MDC.put(MDC_SERVICE_NAME, SERVICE_NAME_VALUE);
			MDC.put(MDC_SESSION_ID, resolvedStreamName);

			logger.info("pipeline event=stream_end streamName={} isRecord={} isAppend={}",
					resolvedStreamName, isRecord, isAppend);
		}
		finally
		{
			MDC.remove(MDC_SESSION_ID);
			MDC.remove(MDC_SERVICE_NAME);
		}
	}

	@Override
	default void onPause(IMediaStream stream, boolean isPause, double location) {}

	@Override
	default void onSeek(IMediaStream stream, double location) {}

	@Override
	default void onStop(IMediaStream stream) {}
}