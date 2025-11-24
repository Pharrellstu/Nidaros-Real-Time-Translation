package com.nidaros.wowza.subtitle;

import com.wowza.wms.application.*;
import com.wowza.wms.logging.WMSLoggerFactory;
import com.wowza.wms.logging.WMSLogger;
import com.wowza.wms.module.ModuleBase;

public class SubtitleModule extends ModuleBase
{

    private static final WMSLogger logger = WMSLoggerFactory.getLogger(SubtitleModule.class);

    public SubtitleModule() {
        logger.info("========================================");
        logger.info("SubtitleModule: Constructor called!");
        logger.info("========================================");
    }

    // This will be called when the module is loaded
    public void init(IApplicationInstance appInstance) {
        logger.info("========================================");
        logger.info("SubtitleModule: INITIALIZED!");
        logger.info("Application: " + appInstance.getApplication().getName());
        logger.info("========================================");
    }
}