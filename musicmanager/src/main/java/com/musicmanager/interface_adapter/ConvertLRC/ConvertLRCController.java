package com.musicmanager.interface_adapter.ConvertLRC;

import java.util.List;

import com.musicmanager.entity.Music;
import com.musicmanager.use_case.ConvertLRC.ConvertLRCInputBoundary;
import com.musicmanager.use_case.ConvertLRC.ConvertLRCInputData;

public class ConvertLRCController {
    private ConvertLRCInputBoundary interactor;

    public ConvertLRCController(ConvertLRCInputBoundary interactor) {
        this.interactor = interactor;
    }

    /* execute ConvertLRC use case for the given songs (all of them, or just one) */
    public void execute(List<Music> musics, List<String> formats) {
        ConvertLRCInputData inputData = new ConvertLRCInputData(musics, formats);
        interactor.execute(inputData);
    }
}
