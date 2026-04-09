(function(global){
  global.WorkerUi={
    refresh(){
      if(typeof global.renderWorkerPipeList==='function') global.renderWorkerPipeList();
    }
  };
})(window);
