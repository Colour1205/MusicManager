package com.musicmanager.use_case.AppSettings;

import com.musicmanager.entity.AppSettings;

public interface AppSettingsDataAccessInterface {

    /**
     * loads persisted settings, or an AppSettings with an empty path and no
     * formats if none have been saved yet
     *
     * @return
     */
    AppSettings load();

    void save(AppSettings settings);
}
